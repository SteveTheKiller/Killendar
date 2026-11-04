using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Killendar.Services
{
    internal static class IcsTimeZoneWriter
    {
        /// <summary>Years of transitions written when the caller gives no span: from the start of
        /// the Windows-era rules to this many years ahead. Zones usually carry a rule running to
        /// year 9999, and expanding it whole wrote ~20,000 RDATEs per zone.</summary>
        private const int DefaultYearsAhead = 50;
        private const int DefaultFirstYear = 1970;

        /// <param name="firstYear">First year to write transitions for (default 1970).</param>
        /// <param name="lastYear">Last year to write transitions for (default now + 50).</param>
        internal static string Build(string id, TimeZoneInfo zone, int? firstYear = null, int? lastYear = null)
        {
            int windowStart = Math.Max(1, firstYear ?? DefaultFirstYear);
            int windowEnd = Math.Min(9999, lastYear ?? DateTime.UtcNow.Year + DefaultYearsAhead);
            if (id.IndexOfAny(new[] { '\r', '\n', ':', ';', '"', ',' }) >= 0)
                throw new NotSupportedException("The calendar time zone identifier cannot be exported: " + id);
            var candidates = new SortedSet<DateTime>();
            foreach (var rule in zone.GetAdjustmentRules())
            {
                candidates.Add(rule.DateStart.Date);
                if (rule.DateEnd.Date < DateTime.MaxValue.Date) candidates.Add(rule.DateEnd.Date.AddDays(1));
                if (rule.DaylightDelta == TimeSpan.Zero) continue;
                for (int year = Math.Max(rule.DateStart.Year, windowStart); year <= Math.Min(rule.DateEnd.Year, windowEnd); year++)
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

            // A candidate within six hours of the last one kept adds nothing: the sweep around the
            // kept one covers two days either side. Some zones put a rule boundary and a change an
            // hour apart (Sao Tome in 2018, Bangladesh at the end of 2009).
            // Around a merged candidate the probe steps by minutes, so two changes close
            // together cannot hide between probes.
            var kept = new List<(DateTime When, bool Dense)>();
            foreach (var candidate in candidates)
            {
                if (kept.Count > 0 && candidate - kept[kept.Count - 1].When < TimeSpan.FromHours(6))
                    kept[kept.Count - 1] = (kept[kept.Count - 1].When, true);
                else kept.Add((candidate, false));
            }

            var transitions = new SortedDictionary<long, (DateTime Local, TimeSpan Before, TimeSpan After, bool Daylight)>();
            long margin = TimeSpan.FromDays(2).Ticks;
            foreach (var (candidate, dense) in kept)
            {
                long step = dense ? TimeSpan.FromMinutes(15).Ticks : TimeSpan.FromHours(6).Ticks;
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
                        var from = Offset(high - 1);
                        var to = Offset(high);
                        // Windows writes an end-of-day change as 23:59:59.999; the real change is
                        // the next whole second, where the new offset already holds.
                        if (high % TimeSpan.TicksPerSecond != 0)
                        {
                            long rounded = (high / TimeSpan.TicksPerSecond + 1) * TimeSpan.TicksPerSecond;
                            if (rounded > DateTime.MaxValue.Ticks || Offset(rounded) != to)
                                throw new NotSupportedException("Calendar time zone has a subsecond transition that cannot be exported: " + id);
                            high = rounded;
                        }
                        var utc = new DateTime(high, DateTimeKind.Utc);
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
