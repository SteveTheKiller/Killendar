using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Killendar.Services
{
    internal static class IcsTimeZoneWriter
    {
        internal static string Build(string id, TimeZoneInfo zone)
        {
            if (id.IndexOfAny(new[] { '\r', '\n', ':', ';', '"', ',' }) >= 0)
                throw new NotSupportedException("The calendar time zone identifier cannot be exported: " + id);
            var candidates = new SortedSet<DateTime>();
            foreach (var rule in zone.GetAdjustmentRules())
            {
                candidates.Add(rule.DateStart.Date);
                if (rule.DateEnd.Date < DateTime.MaxValue.Date) candidates.Add(rule.DateEnd.Date.AddDays(1));
                if (rule.DaylightDelta == TimeSpan.Zero) continue;
                for (int year = rule.DateStart.Year; year <= rule.DateEnd.Year; year++)
                {
                    Add(rule.DaylightTransitionStart, year, rule);
                    Add(rule.DaylightTransitionEnd, year, rule);
                }
            }
            void Add(TimeZoneInfo.TransitionTime transition, int year, TimeZoneInfo.AdjustmentRule rule)
            {
                int last = DateTime.DaysInMonth(year, transition.Month);
                int day = Math.Min(transition.Day, last);
                if (!transition.IsFixedDateRule)
                {
                    var first = new DateTime(year, transition.Month, 1);
                    day = 1 + ((int)transition.DayOfWeek - (int)first.DayOfWeek + 7) % 7 + (transition.Week - 1) * 7;
                    if (day > last) day -= 7;
                }
                var date = new DateTime(year, transition.Month, day) + transition.TimeOfDay.TimeOfDay;
                if (date.Date >= rule.DateStart.Date && date.Date <= rule.DateEnd.Date) candidates.Add(date);
            }

            // The probe interval is shorter than the supported separation between distinct
            // rule candidates. Reject dense definitions rather than miss an offset round trip.
            DateTime? previous = null;
            foreach (var candidate in candidates)
            {
                if (previous.HasValue && candidate - previous.Value < TimeSpan.FromHours(6))
                    throw new NotSupportedException("Calendar time zone transitions are too close to export safely: " + id);
                previous = candidate;
            }

            var transitions = new SortedDictionary<long, (DateTime Local, TimeSpan Before, TimeSpan After, bool Daylight)>();
            long margin = TimeSpan.FromDays(2).Ticks;
            long step = TimeSpan.FromHours(6).Ticks;
            foreach (var candidate in candidates)
            {
                long left = Math.Max(1, candidate.Ticks - margin);
                long stop = candidate.Ticks + Math.Min(margin, DateTime.MaxValue.Ticks - candidate.Ticks);
                var before = Offset(left);
                while (left < stop)
                {
                    long right = left + Math.Min(step, stop - left);
                    var after = Offset(right);
                    if (before != after)
                    {
                        long low = left, high = right;
                        while (high - low > 1)
                        {
                            long middle = low + (high - low) / 2;
                            if (Offset(middle) == before) low = middle; else high = middle;
                        }
                        var utc = new DateTime(high, DateTimeKind.Utc);
                        var from = Offset(high - 1);
                        var to = Offset(high);
                        if (high % TimeSpan.TicksPerSecond != 0)
                            throw new NotSupportedException("Calendar time zone has a subsecond transition that cannot be exported: " + id);
                        DateTime local;
                        try { local = DateTime.SpecifyKind(utc + from, DateTimeKind.Unspecified); }
                        catch (ArgumentOutOfRangeException)
                        {
                            throw new NotSupportedException("Calendar time zone transition exceeds the supported date range: " + id);
                        }
                        transitions[high] = (local, from, to, zone.IsDaylightSavingTime(utc));
                    }
                    left = right;
                    before = after;
                }
            }
            TimeSpan Offset(long ticks) => zone.GetUtcOffset(new DateTime(ticks, DateTimeKind.Utc));

            var output = new StringBuilder();
            output.AppendLine("BEGIN:VTIMEZONE");
            output.Append("TZID:").AppendLine(id);
            var initial = Offset(0);
            Write("STANDARD", new[] { DateTime.MinValue }, initial, initial);
            var groups = new Dictionary<(TimeSpan Before, TimeSpan After, bool Daylight), List<DateTime>>();
            foreach (var transition in transitions.Values)
            {
                var key = (transition.Before, transition.After, transition.Daylight);
                if (!groups.TryGetValue(key, out var dates)) groups[key] = dates = new List<DateTime>();
                dates.Add(transition.Local);
            }
            foreach (var group in groups)
                Write(group.Key.Daylight ? "DAYLIGHT" : "STANDARD", group.Value, group.Key.Before, group.Key.After);
            output.AppendLine("END:VTIMEZONE");
            return output.ToString();

            void Write(string component, IList<DateTime> dates, TimeSpan from, TimeSpan to)
            {
                output.Append("BEGIN:").AppendLine(component);
                output.Append("DTSTART:").AppendLine(dates[0].ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
                output.Append("TZOFFSETFROM:").AppendLine(FormatOffset(from));
                output.Append("TZOFFSETTO:").AppendLine(FormatOffset(to));
                if (dates.Count > 1)
                {
                    output.Append("RDATE:");
                    for (int i = 1; i < dates.Count; i++)
                    {
                        if (i > 1) output.Append(',');
                        output.Append(dates[i].ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
                    }
                    output.AppendLine();
                }
                output.Append("END:").AppendLine(component);
            }
        }

        private static string FormatOffset(TimeSpan offset)
        {
            if (offset.Ticks % TimeSpan.TicksPerSecond != 0 || offset.Duration() > TimeSpan.FromHours(24))
                throw new NotSupportedException("Calendar time zone offset cannot be represented in iCalendar.");
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            offset = offset.Duration();
            return sign + ((int)offset.TotalHours).ToString("D2", CultureInfo.InvariantCulture) +
                offset.Minutes.ToString("D2", CultureInfo.InvariantCulture) +
                (offset.Seconds == 0 ? "" : offset.Seconds.ToString("D2", CultureInfo.InvariantCulture));
        }
    }
}
