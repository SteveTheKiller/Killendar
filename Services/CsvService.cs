using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
            File.WriteAllText(path, BuildCsv(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        public static string BuildCsv(IEnumerable<CalendarEvent> rows)
        {
            var sb = new StringBuilder();
            sb.Append("Subject,Start Date,Start Time,End Date,End Time,All day event,Description,Location,Categories\r\n");

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

                sb.Append(Escape(ev.Title)).Append(',')
                  .Append(startDate).Append(',')
                  .Append(startTime).Append(',')
                  .Append(endDate).Append(',')
                  .Append(endTime).Append(',')
                  .Append(ev.AllDay ? "TRUE" : "FALSE").Append(',')
                  .Append(Escape(ev.Description)).Append(',')
                  .Append(Escape(ev.Location)).Append(',')
                  .Append(Escape(ev.Categories)).Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>RFC 4180: a field containing a comma, a quote or a line break is quoted,
        /// and quotes inside it are doubled. Everything else is written bare.</summary>
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny([',', '"', '\n', '\r']) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // ── Import ────────────────────────────────────────────────────────────

        public static CsvParseResult ParseFile(string path)
        {
            // ReadAllText detects and strips a BOM on its own, so Excel-written UTF-8 and
            // Killendar's own export both arrive clean.
            return ParseText(File.ReadAllText(path));
        }

        public static CsvParseResult ParseText(string text)
        {
            var result  = new CsvParseResult();
            var records = ParseRecords(text);
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

            for (int r = 1; r < records.Count; r++)
            {
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

                if (allDay)
                {
                    ev.Start = startDate;
                    // Inclusive on disk, exclusive in the model - see the export note above.
                    ev.End = TryDate(endD, out var lastDay) && lastDay >= startDate
                        ? lastDay.AddDays(1)
                        : startDate.AddDays(1);
                }
                else
                {
                    ev.Start = startDate + (TryTime(startT, out var st) ? st : TimeSpan.Zero);
                    var endDate = TryDate(endD, out var ed) ? ed : startDate;
                    ev.End = endDate + (TryTime(endT, out var et) ? et : TimeSpan.Zero);
                    if (ev.End <= ev.Start) ev.End = ev.Start.AddHours(1);
                }

                // The Id is DERIVED from the content rather than random, so importing the same
                // file twice skips the second copy in ImportEvents' existing Id check - a CSV
                // carries no UID of its own to dedupe on. Two genuinely identical rows in one
                // file collapse to one appointment, which is the right reading of a duplicate.
                ev.Id = StableId(ev.Title, ev.Start, ev.End, ev.Location);

                result.Events.Add(ev);
            }

            return result;
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
        private static Guid StableId(string title, DateTime start, DateTime end, string location)
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
        private static List<List<string>> ParseRecords(string text)
        {
            var records = new List<List<string>>();
            var fields  = new List<string>();
            var sb      = new StringBuilder();
            bool inQuotes = false;

            int i = 0;
            while (i < text.Length)
            {
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
