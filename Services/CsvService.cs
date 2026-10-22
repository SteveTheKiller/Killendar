using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// What a CSV parse produced, including what it could not keep - same philosophy as
    /// IcsParseResult: an appointment that vanishes silently is worse than one that never
    /// imported.
    /// </summary>
    public sealed class CsvParseResult
    {
        /// <summary>The appointments Killendar can actually store.</summary>
        public List<CalendarEvent> Events = [];

        /// <summary>Rows dropped for having no readable start date.</summary>
        public int Unreadable;

        /// <summary>Rows skipped because a start time, end date or end time was present but could
        /// not be read, or put the end before the start. A blank optional value is not malformed;
        /// it gets the documented default.</summary>
        public int Malformed;

        /// <summary>False when the first line is not the Outlook column set. Nothing was read:
        /// guessing at arbitrary columns means guessing what "Date" means and getting it wrong,
        /// so a file this parser does not recognize is refused with a clear message instead.</summary>
        public bool HeaderValid = true;
    }

    /// <summary>
    /// CSV import and export in Outlook's calendar format - the one schema worth targeting by
    /// name, because it is what Outlook exports and what Google's importer documents:
    ///
    ///   Subject, Start Date, Start Time, End Date, End Time, All day event, Description, Location
    ///
    /// Import matches columns BY NAME, case-insensitively, so a file carrying extra Outlook
    /// columns (Categories, Reminder, ...) still reads - the extras are ignored, except
    /// Categories, which Killendar can store. Export writes the same columns plus Categories.
    ///
    /// A CSV row is a single dated appointment by definition - the format has no way to say
    /// "repeats weekly". Export therefore writes one row per OCCURRENCE (the caller hands in an
    /// already-expanded range), and import creates plain appointments only.
    /// </summary>
    public static class CsvService
    {
        // ── Export ────────────────────────────────────────────────────────────

        /// <summary>
        /// UTF-8 WITH a BOM, unlike the .ics writer: this file's destination is Excel, which
        /// reads a BOM-less CSV in the ANSI codepage and mangles every non-ASCII character.
        /// </summary>
        public static void ExportToFile(IEnumerable<CalendarEvent> rows, string path)
        {
            FilePublication.WriteText(path, BuildCsv(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        public static string BuildCsv(IEnumerable<CalendarEvent> rows)
        {
            var sb = new StringBuilder();
            sb.Append("Subject,Start Date,Start Time,End Date,End Time,All day event,Description,Location,Categories," +
                      "Reminder on/off,Reminder Date,Reminder Time\r\n");

            foreach (var ev in rows)
            {
                // Dates are ISO and times 24-hour, culture-invariant: they parse in Excel, in
                // Google's importer and back into Killendar whatever the machine's locale, where
                // a localized date only round-trips on machines set the same way.
                //
                // All-day end dates are written INCLUSIVE (a one-day event starts and ends on the
                // same date), matching Outlook's convention. Internally the end is exclusive;
                // the import below converts back.
                string startDate = ev.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string endDate, startTime, endTime;

                if (ev.AllDay)
                {
                    var lastDay = ev.End.Date > ev.Start.Date ? ev.End.Date.AddDays(-1) : ev.Start.Date;
                    endDate   = lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    startTime = "";
                    endTime   = "";
                }
                else
                {
                    endDate   = ev.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    startTime = ev.Start.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                    endTime   = ev.End.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                }

                sb.Append(SpreadsheetField(ev.Title)).Append(',')
                  .Append(startDate).Append(',')
                  .Append(startTime).Append(',')
                  .Append(endDate).Append(',')
                  .Append(endTime).Append(',')
                  .Append(ev.AllDay ? "TRUE" : "FALSE").Append(',')
                  .Append(SpreadsheetField(ev.Description)).Append(',')
                  .Append(SpreadsheetField(ev.Location)).Append(',')
                  .Append(SpreadsheetField(ev.Categories)).Append(',');
                // Outlook's reminder columns: the switch, then the moment it fires.
                if (Reminders.FireTime(ev) is DateTime fire)
                    sb.Append("TRUE,").Append(fire.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                      .Append(fire.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("\r\n");
                else
                    sb.Append("FALSE,,\r\n");
            }

            return sb.ToString();
        }

        public static bool RequiresSpreadsheetProtection(IEnumerable<CalendarEvent> rows)
            => rows.Any(row => NeedsProtection(row.Title) || NeedsProtection(row.Description) ||
                               NeedsProtection(row.Location) || NeedsProtection(row.Categories));

        private static bool NeedsProtection(string? value)
        {
            string text = value ?? "";
            int first = 0;
            while (first < text.Length && (char.IsWhiteSpace(text[first]) || char.IsControl(text[first]))) first++;
            if (first >= text.Length || "=+-@".IndexOf(text[first]) < 0) return false;
            bool loneSign = (text[first] == '-' || text[first] == '+') && text.Substring(first + 1).Trim().Length == 0;
            return !loneSign;
        }

        // Match the family spreadsheet policy: put protection before leading controls and
        // quote every text cell. Calendar importers can retain the apostrophe as literal text.
        private static string SpreadsheetField(string? value)
        {
            string text = value ?? "";
            if (NeedsProtection(text)) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        // ── Import ────────────────────────────────────────────────────────────

        public static int ImportEvents(EventStore store, IEnumerable<CalendarEvent> events) =>
            store.ImportEvents(Importable(store, events));

        /// <summary>The rows that are not already present under the fingerprint older Killendars
        /// used for CSV rows.</summary>
        public static List<CalendarEvent> Importable(EventStore store, IEnumerable<CalendarEvent> events)
        {
            var existing = store.Events.ToDictionary(e => e.Id);
            return [.. events.Where(row =>
            {
                if (!existing.TryGetValue(LegacyStableId(row.Title, row.Start, row.End, row.Location), out var old))
                    return true;
                // Recognize exact rows imported by the older fingerprint without rewriting
                // their identities or hiding another row that differs in a stored CSV field.
                return old.Repeat != RepeatFreq.None || old.SeriesId.HasValue || old.Attendees.Count != 0 ||
                       StableId(old) != row.Id;
            })];
        }

        public static CsvParseResult ParseFile(string path) => ParseFile(path, CancellationToken.None);

        /// <summary>Refuses a file over ImportLimits before reading it.</summary>
        public static CsvParseResult ParseFile(string path, CancellationToken cancel)
        {
            ImportLimits.CheckFile(path);
            // ReadAllText detects and strips a BOM on its own, so Excel-written UTF-8 and
            // Killendar's own export both arrive clean.
            return ParseText(File.ReadAllText(path), cancel);
        }

        public static CsvParseResult ParseText(string text) => ParseText(text, CancellationToken.None);

        public static CsvParseResult ParseText(string text, CancellationToken cancel)
        {
            var result  = new CsvParseResult();
            var records = ParseRecords(text, cancel);
            if (records.Count == 0) { result.HeaderValid = false; return result; }

            // Columns are found BY NAME so Outlook's full export (22 columns) reads as well as
            // the minimal 8. Subject and Start Date are the two that make a row an appointment;
            // a first line without both is not this format.
            var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < records[0].Count; i++)
            {
                string name = records[0][i].Trim();
                if (name.Length > 0 && !col.ContainsKey(name)) col[name] = i;
            }
            if (!col.ContainsKey("Subject") || !col.ContainsKey("Start Date"))
            {
                result.HeaderValid = false;
                return result;
            }

            ImportLimits.CheckCount(records.Count - 1);
            for (int r = 1; r < records.Count; r++)
            {
                if ((r & 1023) == 0) cancel.ThrowIfCancellationRequested();
                var row = records[r];

                // A trailing newline yields one empty record; blank lines likewise. Not data.
                if (row.Count == 1 && row[0].Trim().Length == 0) continue;

                string subject  = Field(row, col, "Subject");
                string startD   = Field(row, col, "Start Date");
                string startT   = Field(row, col, "Start Time");
                string endD     = Field(row, col, "End Date");
                string endT     = Field(row, col, "End Time");
                string allDayS  = Field(row, col, "All day event");
                string desc     = Field(row, col, "Description");
                string location = Field(row, col, "Location");
                string cats     = Field(row, col, "Categories");
                string remindOn = Field(row, col, "Reminder on/off");
                string remindD  = Field(row, col, "Reminder Date");
                string remindT  = Field(row, col, "Reminder Time");

                // No readable start date is a counted failure, never a guess - the .ics parser
                // learned that lesson when a malformed file dropped appointments onto "today".
                if (!TryDate(startD, out var startDate)) { result.Unreadable++; continue; }

                bool allDay = IsTrue(allDayS);

                var ev = new CalendarEvent
                {
                    Title       = subject,
                    AllDay      = allDay,
                    Description = desc,
                    Location    = location,
                    Categories  = EventStore.NormalizeCategories(cats)
                };

                // A value that is present has to be readable. Defaults below only stand in for
                // BLANK optional fields; a supplied value that cannot be read, or an end before the
                // start, skips the row rather than moving the appointment somewhere it never was.
                // All-day rows ignore the time columns, which Outlook fills with midnight anyway.
                bool hasEndDate = endD.Length > 0, hasEndTime = endT.Length > 0;
                DateTime ed = default;
                TimeSpan st = TimeSpan.Zero, et = TimeSpan.Zero;
                if ((hasEndDate && !TryDate(endD, out ed)) ||
                    (!allDay && startT.Length > 0 && !TryTime(startT, out st)) ||
                    (!allDay && hasEndTime && !TryTime(endT, out et)))
                {
                    result.Malformed++;
                    continue;
                }

                try
                {
                    if (allDay)
                    {
                        ev.Start = startDate;
                        // Inclusive on disk, exclusive in the model - see the export note above.
                        ev.End = (hasEndDate ? ed : startDate).AddDays(1);
                    }
                    else
                    {
                        ev.Start = startDate + st;
                        // A blank End Time is the default one-hour length, on the end date when
                        // one is given.
                        ev.End = (hasEndDate ? ed : startDate) + (hasEndTime ? et : st + TimeSpan.FromHours(1));
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    result.Unreadable++;
                    continue;
                }
                if (ev.End <= ev.Start)
                {
                    result.Malformed++;
                    continue;
                }

                // The Id is DERIVED from the content rather than random, so importing the same
                // file twice skips the second copy in ImportEvents' existing Id check - a CSV
                // carries no UID of its own to dedupe on. Two genuinely identical rows in one
                // file collapse to one appointment, which is the right reading of a duplicate.
                ev.Id = StableId(ev);
                ev.ReminderMinutes = ReminderFrom(ev, remindOn, remindD, remindT);

                result.Events.Add(ev);
            }

            return result;
        }

        /// <summary>Outlook's reminder: on, at a date and time before the start. On with no
        /// time is Outlook's default 15 minutes; a time after the start sets none.</summary>
        private static int? ReminderFrom(CalendarEvent ev, string on, string date, string time)
        {
            if (!IsTrue(on)) return null;
            if (date.Length == 0 || !TryDate(date, out var day)) return ev.AllDay ? 1440 : 15;
            var t = TimeSpan.Zero;
            if (time.Length > 0 && !TryTime(time, out t)) return ev.AllDay ? 1440 : 15;
            double minutes = (ev.Start - (day + t)).TotalMinutes;
            if (minutes < 0 || minutes > Reminders.MaxMinutes) return null;
            return (int)Math.Round(minutes);
        }

        private static string Field(List<string> row, Dictionary<string, int> col, string name)
        {
            if (!col.TryGetValue(name, out int i) || i >= row.Count) return "";
            return row[i].Trim();
        }

        /// <summary>TRUE is what Outlook writes; 1 and YES cover hand-made files.</summary>
        private static bool IsTrue(string s)
            => s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
               s.Equals("YES",  StringComparison.OrdinalIgnoreCase) ||
               s == "1";

        /// <summary>
        /// ISO first because it is unambiguous, then the machine's own culture - which is the
        /// culture Outlook exported in - then the app's chosen date format.
        /// </summary>
        private static bool TryDate(string s, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(s)) return false;

            if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out date))
            { date = date.Date; return true; }

            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
            { date = date.Date; return true; }

            if (DateFormatManager.TryParse(s, out date))
            { date = date.Date; return true; }

            return false;
        }

        private static bool TryTime(string s, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;

            // DateTime.TryParse reads "14:30", "14:30:00" and "2:30 PM" alike; TimeSpan.TryParse
            // would refuse the AM/PM form Outlook writes on a US machine.
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var dt) ||
                DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out dt))
            {
                time = dt.TimeOfDay;
                return true;
            }
            return false;
        }

        /// <summary>Content-derived Guid: MD5 of the fields that make a row THIS appointment.
        /// MD5 is fine here - this is a fingerprint for dedupe, not security.</summary>
        private static Guid StableId(CalendarEvent row)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write("Killendar CSV identity 2");
                writer.Write(row.Title ?? "");
                writer.Write(row.Start.Ticks);
                writer.Write(row.End.Ticks);
                writer.Write(row.AllDay);
                writer.Write(row.Description ?? "");
                writer.Write(row.Location ?? "");
                writer.Write(EventStore.NormalizeCategories(row.Categories));
            }
            using var md5 = MD5.Create();
            return new Guid(md5.ComputeHash(bytes.ToArray()));
        }

        private static Guid LegacyStableId(string title, DateTime start, DateTime end, string location)
        {
            string key = title + "\n" + start.Ticks.ToString(CultureInfo.InvariantCulture)
                       + "\n" + end.Ticks.ToString(CultureInfo.InvariantCulture) + "\n" + location;
            using var md5 = MD5.Create();
            return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(key)));
        }

        /// <summary>
        /// RFC 4180 state machine. A quoted field may contain commas, quotes (doubled) and line
        /// breaks - which is why this cannot be a Split('\n') and a Split(','): a two-line
        /// Description would shear the file. Line breaks inside a quoted field are normalized
        /// to '\n'; CR outside one is dropped as half of a CRLF.
        /// </summary>
        private static List<List<string>> ParseRecords(string text, CancellationToken cancel)
        {
            var records = new List<List<string>>();
            var fields  = new List<string>();
            var sb      = new StringBuilder();
            bool inQuotes = false;

            int i = 0;
            while (i < text.Length)
            {
                if ((i & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        inQuotes = false; i++; continue;
                    }
                    if (c == '\r') { i++; continue; }
                    sb.Append(c); i++; continue;
                }

                if (c == '"')  { inQuotes = true; i++; continue; }
                if (c == ',')  { fields.Add(sb.ToString()); sb.Clear(); i++; continue; }
                if (c == '\r') { i++; continue; }
                if (c == '\n')
                {
                    fields.Add(sb.ToString()); sb.Clear();
                    records.Add(fields); fields = [];
                    i++; continue;
                }

                sb.Append(c); i++;
            }

            if (sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                records.Add(fields);
            }

            return records;
        }
    }
}
