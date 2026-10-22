using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Killendar.Services
{
    /// <summary>
    /// A time zone a calendar file defines for itself (VTIMEZONE) under a name Windows does not
    /// know: Outlook's "Customized Time Zone", an exported zone with a made-up TZID. The file's
    /// own rules say what the offset is when, so the times in it can still be read exactly.
    ///
    /// Where Windows has a zone that keeps the same offsets at the same moments, the appointment
    /// takes that zone and behaves like any other zoned appointment, repeats included. Where none
    /// matches, a single appointment is still placed exactly; a repeat keeps the times of its
    /// first date on UTC's clock, which the import counts and says.
    /// </summary>
    internal sealed class IcsCustomZone
    {
        internal string Id = "";
        internal string Location = "";   // X-LIC-LOCATION, often an IANA name

        /// <summary>Every change of offset, by the UTC instant it happens, with the offset after it.</summary>
        private readonly List<(DateTime Utc, TimeSpan Offset)> _changes = [];
        private TimeSpan _initial;

        /// <summary>True when the offset changes at all (summer time).</summary>
        internal bool ChangesOffset => _changes.Select(c => c.Offset).Distinct().Count() > 1;

        internal TimeSpan OffsetAt(DateTime utc)
        {
            var offset = _initial;
            foreach (var (when, after) in _changes)
            {
                if (when > utc) break;
                offset = after;
            }
            return offset;
        }

        /// <summary>
        /// A wall-clock time in this zone as UTC. A time that happens twice is the first one; a
        /// time the clocks skip is read with the offset in force before the gap, as RFC 5545 says.
        /// </summary>
        internal DateTime ToUtc(DateTime wall)
        {
            wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
            var offsets = _changes.Select(c => c.Offset).Append(_initial).Distinct()
                .OrderByDescending(o => o).ToList();   // larger offset first: the earlier instant
            foreach (var offset in offsets)
            {
                var utc = wall - offset;
                if (OffsetAt(utc) == offset) return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            }
            // In a gap: the offset half a day before it.
            return DateTime.SpecifyKind(wall - OffsetAt((wall - _initial).AddHours(-12)), DateTimeKind.Utc);
        }

        /// <summary>Reads one VTIMEZONE block (its lines, BEGIN to END). Null when it says nothing usable.</summary>
        internal static IcsCustomZone? Read(List<string> block)
        {
            var zone = new IcsCustomZone();
            var parts = new List<Dictionary<string, string>>();
            Dictionary<string, string>? part = null;
            foreach (var line in block)
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string head = line.Substring(0, colon);
                string name = head.Split(';')[0].Trim().ToUpperInvariant();
                string value = line.Substring(colon + 1).Trim();
                if (name == "BEGIN" && (value.Equals("STANDARD", StringComparison.OrdinalIgnoreCase) ||
                                        value.Equals("DAYLIGHT", StringComparison.OrdinalIgnoreCase)))
                { part = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); continue; }
                if (name == "END" && part != null && (value.Equals("STANDARD", StringComparison.OrdinalIgnoreCase) ||
                                                      value.Equals("DAYLIGHT", StringComparison.OrdinalIgnoreCase)))
                { parts.Add(part); part = null; continue; }
                if (part != null)
                {
                    // RDATE may repeat; keep them all.
                    part[name] = part.TryGetValue(name, out var had) && name == "RDATE" ? had + "," + value : value;
                }
                else if (name == "TZID") zone.Id = value.Trim('"');
                else if (name == "X-LIC-LOCATION") zone.Location = value;
            }
            if (zone.Id.Length == 0 || parts.Count == 0) return null;

            var changes = new List<(DateTime, TimeSpan)>();
            DateTime earliest = DateTime.MaxValue;
            TimeSpan earliestFrom = TimeSpan.Zero;
            foreach (var p in parts)
            {
                if (!p.TryGetValue("TZOFFSETTO", out var toText) || !TryOffset(toText, out var to)) return null;
                var from = p.TryGetValue("TZOFFSETFROM", out var fromText) && TryOffset(fromText, out var f) ? f : to;
                if (!p.TryGetValue("DTSTART", out var startText) || !TryLocal(startText, out var start)) return null;
                if (start < earliest) { earliest = start; earliestFrom = from; }

                var walls = new List<DateTime> { start };
                if (p.TryGetValue("RRULE", out var rule) && !Yearly(rule, start, walls)) return null;
                if (p.TryGetValue("RDATE", out var rdates))
                    foreach (var d in rdates.Split(','))
                        if (TryLocal(d.Trim(), out var extra)) walls.Add(extra);
                foreach (var wall in walls) changes.Add((DateTime.SpecifyKind(wall - from, DateTimeKind.Utc), to));
            }
            zone._initial = earliestFrom;
            zone._changes.AddRange(changes.OrderBy(c => c.Item1));
            return zone;
        }

        /// <summary>A yearly rule as VTIMEZONE writes them: BYMONTH with an ordinal BYDAY ("-1SU")
        /// or a BYMONTHDAY, ending at UNTIL or COUNT, generated through 2100.</summary>
        private static bool Yearly(string rule, DateTime start, List<DateTime> walls)
        {
            int month = start.Month, week = 0, monthDay = 0, count = int.MaxValue;
            DayOfWeek? weekday = null;
            DateTime until = new(2100, 12, 31);
            foreach (var item in rule.Split(';'))
            {
                var kv = item.Split('=');
                if (kv.Length != 2) continue;
                string key = kv[0].Trim().ToUpperInvariant(), val = kv[1].Trim().ToUpperInvariant();
                switch (key)
                {
                    case "FREQ": if (val != "YEARLY") return false; break;
                    case "BYMONTH": if (!int.TryParse(val, out month) || month < 1 || month > 12) return false; break;
                    case "BYMONTHDAY": if (!int.TryParse(val, out monthDay)) return false; break;
                    case "COUNT": if (!int.TryParse(val, out count) || count < 1) return false; break;
                    case "UNTIL":
                        if (TryLocal(val.TrimEnd('Z'), out var u)) until = u; else return false;
                        break;
                    case "BYDAY":
                        if (val.Length < 3) return false;
                        string code = val.Substring(val.Length - 2);
                        string ord = val.Substring(0, val.Length - 2);
                        weekday = code switch
                        {
                            "SU" => DayOfWeek.Sunday, "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday,
                            "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday,
                            "SA" => DayOfWeek.Saturday, _ => null,
                        };
                        if (weekday == null || !int.TryParse(ord.Length == 0 ? "1" : ord, NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture, out week) || week == 0 || week < -1 || week > 5) return false;
                        break;
                }
            }
            if (weekday == null && monthDay == 0) monthDay = start.Day;
            walls.Clear();
            int made = 0;
            for (int year = start.Year; year <= 2100 && made < count; year++)
            {
                DateTime day;
                if (weekday != null)
                {
                    day = week == -1 || week == 5
                        ? Recurrence.NthWeekday(year, month, weekday.Value, -1)
                        : Recurrence.NthWeekday(year, month, weekday.Value, week);
                }
                else
                {
                    if (monthDay > DateTime.DaysInMonth(year, month)) continue;
                    day = new DateTime(year, month, monthDay);
                }
                var wall = day + start.TimeOfDay;
                if (wall < start) continue;
                if (wall > until) break;
                walls.Add(wall);
                made++;
            }
            return true;
        }

        private static bool TryOffset(string text, out TimeSpan offset)
        {
            offset = TimeSpan.Zero;
            text = text.Trim();
            if (text.Length < 5 || (text[0] != '+' && text[0] != '-')) return false;
            if (!int.TryParse(text.Substring(1, 2), out int h) || !int.TryParse(text.Substring(3, 2), out int m)) return false;
            int s = text.Length >= 7 && int.TryParse(text.Substring(5, 2), out int sec) ? sec : 0;
            offset = new TimeSpan(h, m, s);
            if (text[0] == '-') offset = -offset;
            return h <= 23 && m <= 59;
        }

        private static bool TryLocal(string text, out DateTime value) =>
            DateTime.TryParseExact(text.Trim().TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out value) ||
            DateTime.TryParseExact(text.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

        /// <summary>
        /// A Windows zone that keeps this zone's offsets at the same moments, checked through the
        /// years calendars mostly hold. The file's location name wins when it matches, then this
        /// computer's own zone, then the first that does. Null when none does.
        /// </summary>
        internal string? MatchSystemZone()
        {
            bool Same(TimeZoneInfo candidate)
            {
                for (int year = 2015; year <= 2035; year++)
                    for (int month = 1; month <= 12; month++)
                    {
                        var utc = new DateTime(year, month, 15, 12, 0, 0, DateTimeKind.Utc);
                        if (candidate.GetUtcOffset(utc) != OffsetAt(utc)) return false;
                    }
                foreach (var (when, _) in _changes)
                {
                    if (when.Year < 2015 || when.Year > 2035) continue;
                    foreach (var probe in new[] { when.AddHours(-3), when.AddHours(3) })
                        if (candidate.GetUtcOffset(probe) != OffsetAt(probe)) return false;
                }
                return true;
            }

            if (Location.Length > 0 && TimeZoneConverter.TZConvert.TryGetTimeZoneInfo(Location, out var named) && Same(named))
                return named.Id;
            if (Same(TimeZoneInfo.Local)) return TimeZoneInfo.Local.Id;
            return TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(Same)?.Id;
        }
    }
}
