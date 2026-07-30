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
    /// Minimal RFC-5545 iCalendar parser and writer.
    /// Handles VCALENDAR/VEVENT import and export without external dependencies.
    /// </summary>
    public static class IcsService
    {
        // ── Import ────────────────────────────────────────────────────────────

        public static List<CalendarEvent> ParseFile(string path)
        {
            var lines = UnfoldLines(File.ReadAllText(path, Encoding.UTF8));
            return ParseLines(lines);
        }

        public static List<CalendarEvent> ParseText(string icsText)
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

        private static List<CalendarEvent> ParseLines(List<string> lines)
        {
            var events  = new List<CalendarEvent>();
            CalendarEvent? current = null;

            foreach (var raw in lines)
            {
                var (name, param, value) = SplitProperty(raw);

                switch (name.ToUpperInvariant())
                {
                    case "BEGIN":
                        if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                            current = new CalendarEvent();
                        break;

                    case "END":
                        if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && current != null)
                        {
                            // Ensure End is set and after Start
                            if (current.End <= current.Start)
                                current.End = current.AllDay
                                    ? current.Start.AddDays(1)
                                    : current.Start.AddHours(1);
                            events.Add(current);
                            current = null;
                        }
                        break;

                    case "UID":
                        if (current != null)
                            if (Guid.TryParse(value, out var g)) current.Id = g;
                        break;

                    case "SUMMARY":
                        if (current != null) current.Title = UnescapeText(value);
                        break;

                    case "DESCRIPTION":
                        if (current != null) current.Description = UnescapeText(value);
                        break;

                    case "LOCATION":
                        if (current != null) current.Location = UnescapeText(value);
                        break;

                    case "DTSTART":
                        if (current != null)
                        {
                            var (dt, allDay) = ParseDateTime(value, param);
                            current.Start  = dt;
                            current.AllDay = allDay;
                        }
                        break;

                    case "DTEND":
                    case "DUE":
                        if (current != null)
                            current.End = ParseDateTime(value, param).dt;
                        break;

                    case "DURATION":
                        if (current != null && current.End == default)
                            current.End = current.Start + ParseDuration(value);
                        break;

                    case "CREATED":
                    case "DTSTAMP":
                        if (current != null)
                            current.Created = ParseDateTime(value, param).dt;
                        break;

                    case "LAST-MODIFIED":
                        if (current != null)
                            current.Modified = ParseDateTime(value, param).dt;
                        break;

                    case "ATTENDEE":
                        if (current != null)
                        {
                            // Value is "mailto:user@example.com" or raw email.
                            // Also check CN= parameter for display name (ignored for now).
                            string addr = value.Trim();
                            if (addr.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                                addr = addr.Substring(7);
                            if (!string.IsNullOrWhiteSpace(addr) && addr.Contains("@"))
                                current.Attendees.Add(addr);
                        }
                        break;

                    case "ORGANIZER":
                        // Could store organizer; for now skip
                        break;
                }
            }

            return events;
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

        private static (DateTime dt, bool allDay) ParseDateTime(string value, string _)
        {
            value = value.Trim();

            // DATE-only format: YYYYMMDD
            if (value.Length == 8 && !value.Contains('T'))
            {
                if (TryParseDate(value, out var d))
                    return (d, true);
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
                    var kind = utc ? DateTimeKind.Utc : DateTimeKind.Local;
                    var dt   = new DateTime(yr, mo, dy, hr, mn, sc, kind);
                    if (utc) dt = dt.ToLocalTime();
                    return (dt, false);
                }
            }

            return (DateTime.Now, false);
        }

        private static bool TryParseDate(string s, out DateTime result)
        {
            result = default;
            if (s.Length < 8) return false;
            if (int.TryParse(s.Substring(0, 4), out int yr) &&
                int.TryParse(s.Substring(4, 2), out int mo) &&
                int.TryParse(s.Substring(6, 2), out int dy))
            {
                result = new DateTime(yr, mo, dy, 0, 0, 0, DateTimeKind.Local);
                return true;
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
                sb.AppendLine($"UID:{ev.Id}");
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

                sb.AppendLine($"SUMMARY:{EscapeText(ev.Title)}");

                if (!string.IsNullOrEmpty(ev.Location))
                    sb.AppendLine(FoldLine($"LOCATION:{EscapeText(ev.Location)}"));

                if (!string.IsNullOrEmpty(ev.Description))
                    sb.AppendLine(FoldLine($"DESCRIPTION:{EscapeText(ev.Description)}"));

                foreach (var attendee in ev.Attendees)
                    sb.AppendLine(FoldLine($"ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:{attendee}"));

                sb.AppendLine("END:VEVENT");
            }

            sb.AppendLine("END:VCALENDAR");
            return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
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
