using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Killendar.Models;

#pragma warning disable IDE0057 // range operator not polyfilled on net48

namespace Killendar.Services
{
    /// <summary>
    /// What a parse produced, INCLUDING what it could not keep. Every counter here exists because
    /// an appointment that vanishes silently is worse than one that never imported: the user only
    /// finds out by missing it. Import reports all of them.
    /// </summary>
    public sealed class IcsParseResult
    {
        /// <summary>The appointments Killendar can actually store.</summary>
        public List<CalendarEvent> Events = [];

        /// <summary>VEVENTs dropped for having no readable start date.</summary>
        public int Unreadable;

        /// <summary>VTODO / VJOURNAL / VFREEBUSY blocks. Killendar has nowhere to put a task,
        /// a journal entry or a free/busy block, so they are dropped - but counted.</summary>
        public int Unsupported;

        /// <summary>VEVENTs carrying RRULE or RDATE - a repeating appointment. Killendar has no
        /// repeat concept, so ONLY THE FIRST OCCURRENCE was imported and every later one is lost.
        /// Counted so the import can say so out loud. Remove this when repeats are modeled.</summary>
        public int Repeating;
    }

    /// <summary>
    /// Minimal RFC-5545 iCalendar parser and writer.
    /// Handles VCALENDAR/VEVENT import and export without external dependencies.
    /// </summary>
    public static class IcsService
    {
        // ── Import ────────────────────────────────────────────────────────────

        public static IcsParseResult ParseFile(string path)
        {
            var lines = UnfoldLines(File.ReadAllText(path, Encoding.UTF8));
            return ParseLines(lines);
        }

        public static IcsParseResult ParseText(string icsText)
        {
            var lines = UnfoldLines(icsText);
            return ParseLines(lines);
        }

        private static List<string> UnfoldLines(string raw)
        {
            // RFC-5545 line unfolding: CRLF + space/tab = continuation
            raw = raw.Replace("\r\n", "\n").Replace("\r", "\n");
            var sb     = new StringBuilder();
            var result = new List<string>();

            foreach (var line in raw.Split('\n'))
            {
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                {
                    sb.Append(line.Substring(1));
                }
                else
                {
                    if (sb.Length > 0) result.Add(sb.ToString());
                    sb.Clear();
                    sb.Append(line);
                }
            }
            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        private static IcsParseResult ParseLines(List<string> lines)
        {
            var result = new IcsParseResult();

            // Open components, innermost last. A property belongs to the component it sits
            // directly inside, and nothing else - see the VALARM note below.
            var stack = new List<string>();

            CalendarEvent? current = null;
            bool haveStart = false;   // no DTSTART means no appointment, however much else parsed
            bool repeating = false;
            DateTime? recurrenceId = null;   // set when this VEVENT replaces one date of a series

            foreach (var raw in lines)
            {
                var (name, param, value) = SplitProperty(raw);
                string upper = name.ToUpperInvariant();

                if (upper == "BEGIN")
                {
                    string comp = value.Trim().ToUpperInvariant();
                    stack.Add(comp);

                    if (comp == "VEVENT" && current == null)
                    {
                        current      = new CalendarEvent();
                        haveStart    = false;
                        repeating    = false;
                        recurrenceId = null;
                    }
                    else if (current == null &&
                             (comp == "VTODO" || comp == "VJOURNAL" || comp == "VFREEBUSY"))
                    {
                        result.Unsupported++;
                    }
                    continue;
                }

                if (upper == "END")
                {
                    string comp = value.Trim().ToUpperInvariant();
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);

                    if (comp == "VEVENT" && current != null)
                    {
                        if (!haveStart)
                        {
                            result.Unreadable++;
                        }
                        else
                        {
                            if (current.End <= current.Start)
                                current.End = current.AllDay
                                    ? current.Start.AddDays(1)
                                    : current.Start.AddHours(1);

                            // Now that UID is final, an override can be pointed at its series. The
                            // row gets a fresh id of its own: the UID names the SERIES, and reusing
                            // it here would make the override collide with the master.
                            if (recurrenceId.HasValue)
                            {
                                current.SeriesId        = current.Id;
                                current.OccurrenceStart = recurrenceId.Value;
                                current.Id              = Guid.NewGuid();
                                current.Repeat          = RepeatFreq.None;
                            }

                            if (repeating) result.Repeating++;
                            result.Events.Add(current);
                        }
                        current = null;
                    }
                    continue;
                }

                if (current == null) continue;

                // A property counts only when VEVENT is the component we are DIRECTLY inside.
                // A VALARM nested in a VEVENT carries its own DESCRIPTION, DURATION and ATTENDEE:
                // without this guard the reminder's text overwrote the appointment's description
                // and the reminder's address was added to the appointment's attendee list.
                if (stack.Count == 0 || stack[stack.Count - 1] != "VEVENT") continue;

                switch (upper)
                {
                    case "UID":
                        if (Guid.TryParse(value, out var g)) current.Id = g;
                        break;

                    case "SUMMARY":
                        current.Title = UnescapeText(value);
                        break;

                    case "DESCRIPTION":
                        current.Description = UnescapeText(value);
                        break;

                    case "LOCATION":
                        current.Location = UnescapeText(value);
                        break;

                    case "CATEGORIES":
                        current.Categories = ParseCategoryList(value);
                        break;

                    // A repeat rule Killendar can hold is read into the appointment. One it cannot
                    // - "the third Monday of the month", an hourly repeat - still imports as a
                    // single date, and the loss is COUNTED so the import can say so rather than
                    // hiding it.
                    case "RRULE":
                        if (!ParseRRule(value, current)) repeating = true;
                        break;

                    // A loose list of extra dates is not a pattern; there is nowhere to put it.
                    case "RDATE":
                        repeating = true;
                        break;

                    // Dates removed from the series one at a time. Exactly Killendar's SkipDates,
                    // so these round-trip properly.
                    case "EXDATE":
                        foreach (var part in value.Split(','))
                        {
                            var ex = ParseDateTime(part, param);
                            if (ex != null) current.SkipDates.Add(ex.Value.dt.Date);
                        }
                        break;

                    // This VEVENT replaces ONE date of a series. Only NOTED here and applied at
                    // END:VEVENT, because the link back to the series is the UID and RFC 5545
                    // does not require UID to come first - resolving it now would attach the
                    // override to whatever id the event happened to have at this point.
                    case "RECURRENCE-ID":
                    {
                        var rid = ParseDateTime(value, param);
                        if (rid != null) recurrenceId = rid.Value.dt;
                        break;
                    }

                    case "DTSTART":
                    {
                        var parsed = ParseDateTime(value, param);
                        if (parsed == null) break;      // haveStart stays false -> counted, not guessed
                        current.Start  = parsed.Value.dt;
                        current.AllDay = parsed.Value.allDay;
                        haveStart      = true;
                        break;
                    }

                    case "DTEND":
                    case "DUE":
                    {
                        var parsed = ParseDateTime(value, param);
                        if (parsed != null) current.End = parsed.Value.dt;
                        break;
                    }

                    case "DURATION":
                        if (current.End == default)
                            current.End = current.Start + ParseDuration(value);
                        break;

                    case "CREATED":
                    case "DTSTAMP":
                    {
                        var parsed = ParseDateTime(value, param);
                        if (parsed != null) current.Created = parsed.Value.dt;
                        break;
                    }

                    case "LAST-MODIFIED":
                    {
                        var parsed = ParseDateTime(value, param);
                        if (parsed != null) current.Modified = parsed.Value.dt;
                        break;
                    }

                    case "ATTENDEE":
                    {
                        // Value is "mailto:user@example.com" or raw email.
                        // Also check CN= parameter for display name (ignored for now).
                        string addr = value.Trim();
                        if (addr.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                            addr = addr.Substring(7);
                        if (!string.IsNullOrWhiteSpace(addr) && addr.Contains("@"))
                            current.Attendees.Add(addr);
                        break;
                    }

                    case "ORGANIZER":
                        // Could store organizer; for now skip
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Reads an RRULE into the appointment. Returns FALSE when the rule says something
        /// Killendar cannot hold, in which case nothing is written and the caller counts the loss.
        ///
        /// Supported: FREQ of DAILY / WEEKLY / MONTHLY / YEARLY, INTERVAL, COUNT, UNTIL, and
        /// BYDAY as plain weekday names. Deliberately NOT supported: an ordinal BYDAY ("3MO" =
        /// the third Monday), BYMONTHDAY, BYSETPOS, BYWEEKNO and the rest. Those are real rules
        /// that Killendar has no way to draw, and half-importing one would put the appointment on
        /// the wrong days - worse than importing it once and saying so.
        /// </summary>
        private static bool ParseRRule(string value, CalendarEvent ev)
        {
            var freq     = RepeatFreq.None;
            int interval = 1;
            int count    = 0;
            DateTime? until = null;
            var days = new List<DayOfWeek>();

            foreach (var part in value.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;

                string key = part.Substring(0, eq).Trim().ToUpperInvariant();
                string val = part.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "FREQ":
                        switch (val.ToUpperInvariant())
                        {
                            case "DAILY":   freq = RepeatFreq.Daily;   break;
                            case "WEEKLY":  freq = RepeatFreq.Weekly;  break;
                            case "MONTHLY": freq = RepeatFreq.Monthly; break;
                            case "YEARLY":  freq = RepeatFreq.Yearly;  break;
                            default: return false;   // HOURLY, MINUTELY, SECONDLY
                        }
                        break;

                    case "INTERVAL":
                        if (!int.TryParse(val, out interval) || interval < 1) return false;
                        break;

                    case "COUNT":
                        if (!int.TryParse(val, out count) || count < 1) return false;
                        break;

                    case "UNTIL":
                    {
                        var u = ParseDateTime(val, "");
                        if (u == null) return false;
                        until = u.Value.dt.Date;
                        break;
                    }

                    case "BYDAY":
                        foreach (var d in val.Split(','))
                        {
                            var day = ParseWeekday(d.Trim());
                            if (day == null) return false;   // ordinal form, e.g. "3MO" or "-1FR"
                            if (!days.Contains(day.Value)) days.Add(day.Value);
                        }
                        break;

                    // Harmless: the week start only matters to rules Killendar does not support.
                    case "WKST":
                        break;

                    default:
                        return false;   // BYMONTHDAY, BYSETPOS, BYMONTH, BYWEEKNO, ...
                }
            }

            if (freq == RepeatFreq.None) return false;

            // BYDAY on anything but a weekly rule means "the Mondays within each month/year",
            // which is an ordinal rule in disguise.
            if (days.Count > 0 && freq != RepeatFreq.Weekly) return false;

            ev.Repeat         = freq;
            ev.RepeatInterval = interval;
            ev.RepeatCount    = count;
            ev.RepeatUntil    = until;
            ev.RepeatDays     = days;
            return true;
        }

        /// <summary>"MO" through "SU", or null for the ordinal form ("3MO") this cannot draw.</summary>
        private static DayOfWeek? ParseWeekday(string s)
        {
            return s.ToUpperInvariant() switch
            {
                "SU" => (DayOfWeek?)DayOfWeek.Sunday,
                "MO" => (DayOfWeek?)DayOfWeek.Monday,
                "TU" => (DayOfWeek?)DayOfWeek.Tuesday,
                "WE" => (DayOfWeek?)DayOfWeek.Wednesday,
                "TH" => (DayOfWeek?)DayOfWeek.Thursday,
                "FR" => (DayOfWeek?)DayOfWeek.Friday,
                "SA" => (DayOfWeek?)DayOfWeek.Saturday,
                _ => null,
            };
        }

        private static string WeekdayCode(DayOfWeek d)
        {
            return d switch
            {
                DayOfWeek.Sunday => "SU",
                DayOfWeek.Monday => "MO",
                DayOfWeek.Tuesday => "TU",
                DayOfWeek.Wednesday => "WE",
                DayOfWeek.Thursday => "TH",
                DayOfWeek.Friday => "FR",
                _ => "SA",
            };
        }

        private static (string name, string param, string value) SplitProperty(string line)
        {
            // Split NAME;PARAM=val:VALUE
            int colon = line.IndexOf(':');
            if (colon < 0) return (line, "", "");

            var left  = line.Substring(0, colon);
            var value = line.Substring(colon + 1);

            int semi = left.IndexOf(';');
            if (semi < 0) return (left, "", value);

            return (left.Substring(0, semi), left.Substring(semi + 1), value);
        }

        /// <summary>
        /// Null when the value is not a date this parser can read.
        ///
        /// It used to return DateTime.Now on failure, which meant a malformed or unusual file
        /// quietly dropped appointments onto TODAY at whatever time the import happened to run,
        /// and nothing anywhere told the user. A date that cannot be read is now a failure the
        /// caller has to deal with, not a guess dressed up as data.
        /// </summary>
        private static (DateTime dt, bool allDay)? ParseDateTime(string value, string _)
        {
            value = value.Trim();
            if (value.Length == 0) return null;

            // DATE-only format: YYYYMMDD
            if (value.Length == 8 && !value.Contains('T'))
            {
                if (TryParseDate(value, out var d))
                    return (d, true);
                return null;
            }

            // DATE-TIME: YYYYMMDDTHHMMSS[Z]
            if (value.Length >= 15 && value.Contains('T'))
            {
                bool utc = value.EndsWith("Z");
                var s    = value.TrimEnd('Z');
                if (s.Length >= 15 &&
                    int.TryParse(s.Substring(0,  4), out int yr)  &&
                    int.TryParse(s.Substring(4,  2), out int mo)  &&
                    int.TryParse(s.Substring(6,  2), out int dy)  &&
                    int.TryParse(s.Substring(9,  2), out int hr)  &&
                    int.TryParse(s.Substring(11, 2), out int mn)  &&
                    int.TryParse(s.Substring(13, 2), out int sc))
                {
                    // Each field parsed as a number and is still nonsense as a date (month 13,
                    // 31 February, hour 25). The ctor throws; that is a bad line, not a crash.
                    try
                    {
                        var kind = utc ? DateTimeKind.Utc : DateTimeKind.Local;
                        var dt   = new DateTime(yr, mo, dy, hr, mn, sc, kind);
                        if (utc) dt = dt.ToLocalTime();
                        return (dt, false);
                    }
                    catch (ArgumentOutOfRangeException) { return null; }
                }
            }

            return null;
        }

        private static bool TryParseDate(string s, out DateTime result)
        {
            result = default;
            if (s.Length < 8) return false;
            if (int.TryParse(s.Substring(0, 4), out int yr) &&
                int.TryParse(s.Substring(4, 2), out int mo) &&
                int.TryParse(s.Substring(6, 2), out int dy))
            {
                try
                {
                    result = new DateTime(yr, mo, dy, 0, 0, 0, DateTimeKind.Local);
                    return true;
                }
                catch (ArgumentOutOfRangeException) { return false; }
            }
            return false;
        }

        private static TimeSpan ParseDuration(string value)
        {
            // Basic DURATION: P[nW][nD][T[nH][nM][nS]]
            var m = Regex.Match(value,
                @"^(?<neg>-)?P(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<mn>\d+)M)?(?:(?<s>\d+)S)?)?$");
            if (!m.Success) return TimeSpan.Zero;

            int weeks   = m.Groups["w"].Success  ? int.Parse(m.Groups["w"].Value)  : 0;
            int days    = m.Groups["d"].Success  ? int.Parse(m.Groups["d"].Value)  : 0;
            int hours   = m.Groups["h"].Success  ? int.Parse(m.Groups["h"].Value)  : 0;
            int minutes = m.Groups["mn"].Success ? int.Parse(m.Groups["mn"].Value) : 0;
            int seconds = m.Groups["s"].Success  ? int.Parse(m.Groups["s"].Value)  : 0;

            var ts = new TimeSpan((weeks * 7) + days, hours, minutes, seconds);
            return m.Groups["neg"].Success ? ts.Negate() : ts;
        }

        private static string UnescapeText(string s)
            => s.Replace("\\n", "\n").Replace("\\N", "\n")
                .Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

        /// <summary>
        /// Splits an RFC-5545 CATEGORIES value. Unlike every other text property, this one is a
        /// comma-separated LIST, so the split has to happen on UNESCAPED commas only: a comma
        /// inside a single category name arrives as "\," and must not break the list. Each part
        /// is unescaped after the split, never before, or the two cases become indistinguishable.
        ///
        /// Killendar's own storage uses commas as its separator too, so a name that survives the
        /// split still containing one has it stripped rather than being allowed to split again
        /// downstream - the same rule the Add and Rename boxes apply.
        /// </summary>
        private static string ParseCategoryList(string value)
        {
            var names = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)   // keep the escape pair intact for the unescape below
                {
                    sb.Append(c).Append(value[i + 1]);
                    i++;
                    continue;
                }
                if (c == ',')
                {
                    names.Add(UnescapeText(sb.ToString()));
                    sb.Clear();
                    continue;
                }
                sb.Append(c);
            }
            names.Add(UnescapeText(sb.ToString()));

            var cleaned = new List<string>();
            foreach (string n in names)
            {
                string t = n.Replace(",", "").Trim();
                if (t.Length > 0) cleaned.Add(t);
            }
            return EventStore.NormalizeCategories(string.Join(", ", cleaned));
        }

        // ── Export ────────────────────────────────────────────────────────────

        public static void ExportToFile(IEnumerable<CalendarEvent> events, string path)
        {
            File.WriteAllText(path, BuildIcs(events), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static string BuildIcs(IEnumerable<CalendarEvent> events)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//killertools.net//Killendar//EN");
            sb.AppendLine("CALSCALE:GREGORIAN");
            sb.AppendLine("METHOD:PUBLISH");

            foreach (var ev in events)
            {
                sb.AppendLine("BEGIN:VEVENT");

                // The UID names the SERIES. A row that replaces one date carries its master's UID
                // plus a RECURRENCE-ID saying which date - that pairing is how every other
                // calendar links the two, and writing the override's own id instead would arrive
                // at the other end as an unrelated appointment sitting on top of the series.
                sb.AppendLine($"UID:{ev.SeriesKey}");
                sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}");
                sb.AppendLine($"CREATED:{ev.Created.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");
                sb.AppendLine($"LAST-MODIFIED:{ev.Modified.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");

                if (ev.AllDay)
                {
                    sb.AppendLine($"DTSTART;VALUE=DATE:{ev.Start:yyyyMMdd}");
                    sb.AppendLine($"DTEND;VALUE=DATE:{ev.End:yyyyMMdd}");
                }
                else
                {
                    sb.AppendLine($"DTSTART:{ev.Start.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");
                    sb.AppendLine($"DTEND:{ev.End.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");
                }

                if (ev.IsOverride && ev.OccurrenceStart.HasValue)
                    sb.AppendLine(ev.AllDay
                        ? $"RECURRENCE-ID;VALUE=DATE:{ev.OccurrenceStart.Value:yyyyMMdd}"
                        : $"RECURRENCE-ID:{ev.OccurrenceStart.Value.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");

                if (ev.IsSeries)
                {
                    sb.AppendLine(BuildRRule(ev));

                    // Dates the user removed one at a time. Written as floating DATE values to
                    // match how they are stored and compared, which is by calendar date.
                    if (ev.SkipDates != null && ev.SkipDates.Count > 0)
                    {
                        var stamps = new List<string>();
                        foreach (var d in ev.SkipDates) stamps.Add(d.ToString("yyyyMMdd"));
                        sb.AppendLine(FoldLine("EXDATE;VALUE=DATE:" + string.Join(",", stamps)));
                    }
                }

                sb.AppendLine($"SUMMARY:{EscapeText(ev.Title)}");

                if (!string.IsNullOrEmpty(ev.Location))
                    sb.AppendLine(FoldLine($"LOCATION:{EscapeText(ev.Location)}"));

                if (!string.IsNullOrEmpty(ev.Description))
                    sb.AppendLine(FoldLine($"DESCRIPTION:{EscapeText(ev.Description)}"));

                foreach (var attendee in ev.Attendees)
                    sb.AppendLine(FoldLine($"ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:{attendee}"));

                // CATEGORIES is a comma-separated LIST, so each name is escaped on its own and
                // the separators are then written raw. Running EscapeText over the whole string
                // would turn every separator into "\," and collapse the list into one category
                // named "Work\, Client" - the mistake this property invites.
                if (!string.IsNullOrEmpty(ev.Categories))
                {
                    var escaped = new List<string>();
                    foreach (string name in EventStore.SplitCategories(ev.Categories))
                        escaped.Add(EscapeText(name));
                    if (escaped.Count > 0)
                        sb.AppendLine(FoldLine("CATEGORIES:" + string.Join(",", escaped)));
                }

                sb.AppendLine("END:VEVENT");
            }

            sb.AppendLine("END:VCALENDAR");
            return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        /// <summary>
        /// The series' repeat rule. COUNT and UNTIL are mutually exclusive in RFC 5545, so only
        /// one is ever written - COUNT wins, matching how the editor treats them.
        /// </summary>
        private static string BuildRRule(CalendarEvent ev)
        {
            var sb = new StringBuilder("RRULE:FREQ=");
            switch (ev.Repeat)
            {
                case RepeatFreq.Daily:   sb.Append("DAILY");   break;
                case RepeatFreq.Weekly:  sb.Append("WEEKLY");  break;
                case RepeatFreq.Monthly: sb.Append("MONTHLY"); break;
                default:                 sb.Append("YEARLY");  break;
            }

            if (ev.RepeatInterval > 1) sb.Append(";INTERVAL=").Append(ev.RepeatInterval);

            if (ev.Repeat == RepeatFreq.Weekly && ev.RepeatDays != null && ev.RepeatDays.Count > 0)
            {
                var codes = new List<string>();
                foreach (var d in ev.RepeatDays) codes.Add(WeekdayCode(d));
                sb.Append(";BYDAY=").Append(string.Join(",", codes));
            }

            if (ev.RepeatCount > 0)
                sb.Append(";COUNT=").Append(ev.RepeatCount);
            else if (ev.RepeatUntil.HasValue)
                sb.Append(";UNTIL=").Append(ev.RepeatUntil.Value.ToString("yyyyMMdd'T'235959'Z'"));

            return sb.ToString();
        }

        private static string EscapeText(string s)
            => s.Replace("\\", "\\\\").Replace(",", "\\,")
                .Replace(";", "\\;").Replace("\n", "\\n");

        /// <summary>RFC-5545 line folding at 75 octets.</summary>
        private static string FoldLine(string line)
        {
            if (line.Length <= 75) return line;
            var sb = new StringBuilder();
            sb.Append(line.Substring(0, 75));
            int index = 75;
            while (index < line.Length)
            {
                sb.Append("\r\n ");
                int chunk = Math.Min(74, line.Length - index);
                sb.Append(line.Substring(index, chunk));
                index += chunk;
            }
            return sb.ToString();
        }
    }
}
