using System;
using System.Collections.Generic;
using System.Linq;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// Find open time: the gaps between appointments that are at least a given length. The search
    /// starts at the next quarter hour from <c>from</c>. With work hours on, only the working hours
    /// of workdays count, and holidays from the holiday calendar are skipped. All-day appointments
    /// never count as busy, because a trip or a birthday does not fill the clock.
    /// </summary>
    internal static class FreeTime
    {
        internal readonly struct Gap
        {
            public Gap(DateTime start, DateTime end) { Start = start; End = end; }
            public DateTime Start { get; }
            public DateTime End { get; }
        }

        /// <summary>The most gaps one search returns, so a month with an empty calendar stays a
        /// short list.</summary>
        internal const int MaxGaps = 60;

        internal static List<Gap> Find(IEnumerable<CalendarEvent> events, DateTime from, DateTime to,
            TimeSpan length, bool workHoursOnly)
        {
            var result = new List<Gap>();
            if (length <= TimeSpan.Zero) return result;
            var start = RoundUpToQuarter(from);
            if (start >= to) return result;

            // Busy blocks, merged where they touch or overlap.
            var busy = new List<(DateTime Start, DateTime End)>();
            foreach (var ev in events.Where(e => !e.AllDay && e.End > start && e.Start < to).OrderBy(e => e.Start))
            {
                if (busy.Count > 0 && ev.Start <= busy[busy.Count - 1].End)
                {
                    var last = busy[busy.Count - 1];
                    if (ev.End > last.End) busy[busy.Count - 1] = (last.Start, ev.End);
                }
                else busy.Add((ev.Start, ev.End));
            }

            foreach (var (winStart, winEnd) in Windows(start, to, workHoursOnly))
            {
                var cursor = winStart;
                foreach (var block in busy)
                {
                    if (block.End <= cursor) continue;
                    if (block.Start >= winEnd) break;
                    if (block.Start > cursor && Add(result, cursor, block.Start, length)) return result;
                    if (block.End > cursor) cursor = block.End;
                    if (cursor >= winEnd) break;
                }
                if (cursor < winEnd && Add(result, cursor, winEnd, length)) return result;
            }
            return result;
        }

        /// <summary>Adds the gap when it is long enough; true once the list is full.</summary>
        private static bool Add(List<Gap> result, DateTime start, DateTime end, TimeSpan length)
        {
            if (end - start >= length) result.Add(new Gap(start, end));
            return result.Count >= MaxGaps;
        }

        /// <summary>The stretches of time a gap may sit in: one per workday's hours, or the whole
        /// range as one stretch when work hours are off.</summary>
        private static IEnumerable<(DateTime Start, DateTime End)> Windows(DateTime from, DateTime to, bool workHoursOnly)
        {
            if (!workHoursOnly)
            {
                yield return (from, to);
                yield break;
            }
            for (var day = from.Date; day < to; day = day.AddDays(1))
            {
                if (WorkSchedule.IsDayOff(day)) continue;
                var s = day.AddHours(WorkSchedule.StartHour);
                var e = day.AddHours(WorkSchedule.EndHour);
                if (s < from) s = from;
                if (e > to) e = to;
                if (s < e) yield return (s, e);
            }
        }

        internal static DateTime RoundUpToQuarter(DateTime t)
        {
            var floor = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute / 15 * 15, 0, t.Kind);
            return floor == t ? t : floor.AddMinutes(15);
        }
    }
}
