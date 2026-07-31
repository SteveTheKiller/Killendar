using System;
using System.Collections.Generic;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// Turns a series master into the individual appointments that fall inside a date range.
    ///
    /// Occurrences are NEVER stored. A weekly standup is one row, and the dates are worked out
    /// each time a view asks for them, which is why changing the series is a single edit and why
    /// a series with no end date does not need infinite rows. The store calls Expand from
    /// GetInRange, so every view and the day agenda get repeats without knowing this file exists.
    ///
    /// Every occurrence handed out is a CLONE of the master carrying:
    ///   - SeriesId       = the master's Id, so an edit knows which series it belongs to;
    ///   - OccurrenceStart= the start THIS occurrence was generated at, which is how a later
    ///     "just this one" edit or delete names the date it means.
    /// Its own Id is left as the master's, so selection and highlight keep working unchanged.
    /// </summary>
    public static class Recurrence
    {
        /// <summary>
        /// Safety net only, not the working limit: the generator jumps straight to the requested
        /// range (see Starts), so a normal repaint takes a handful of steps however old the series
        /// is. This exists so a corrupt interval or an absurd pattern cannot spin forever.
        /// </summary>
        private const int MaxSteps = 20000;

        /// <summary>
        /// Occurrences of <paramref name="master"/> overlapping the half-open interval
        /// [from, to). Returns nothing when the master does not repeat: a plain appointment is
        /// not a one-occurrence series, and the caller handles it directly.
        /// </summary>
        public static IEnumerable<CalendarEvent> Expand(
            CalendarEvent master, DateTime from, DateTime to)
        {
            if (master == null || !master.IsSeries) yield break;

            var duration = master.End > master.Start
                ? master.End - master.Start
                : (master.AllDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));

            int interval = master.RepeatInterval > 0 ? master.RepeatInterval : 1;

            // Compared by DATE. A skip is the user saying "not that day", and it has to survive
            // the series later being moved to a different time of day.
            var skips = new HashSet<DateTime>();
            foreach (var d in master.SkipDates) skips.Add(d.Date);

            // "Stop after N times" has to be counted from the FIRST occurrence, so a series with a
            // count cannot be fast-forwarded - but the count bounds the work by itself. Without a
            // count there is nothing to tally, so generation can jump straight to the range and a
            // ten-year-old daily series costs the same as a new one.
            bool counted  = master.RepeatCount > 0;
            var  jumpTo   = counted ? master.Start : from;

            int emitted = 0;
            int steps   = 0;

            foreach (var start in Starts(master, interval, jumpTo))
            {
                if (++steps > MaxSteps) yield break;

                if (master.RepeatUntil.HasValue && start.Date > master.RepeatUntil.Value.Date)
                    yield break;

                if (counted && emitted >= master.RepeatCount)
                    yield break;

                // A skipped date still consumes its place in the count, exactly as it does in
                // every other calendar: deleting one Tuesday does not add a Tuesday at the end.
                emitted++;

                if (start >= to) yield break;

                if (skips.Contains(start.Date)) continue;

                var end = start + duration;
                if (end <= from) continue;

                var occ = master.Clone();
                occ.Start           = start;
                occ.End             = end;
                occ.SeriesId        = master.Id;
                occ.OccurrenceStart = start;
                yield return occ;
            }
        }

        /// <summary>
        /// True when <paramref name="candidate"/> is a date the series still generates. An
        /// override is only shown when its master would still produce that date, so editing the
        /// series out from under a "just this one" change does not leave the change orphaned.
        /// </summary>
        public static bool GeneratesDate(CalendarEvent master, DateTime candidate)
        {
            if (master == null || !master.IsSeries) return false;

            var day = candidate.Date;
            if (day < master.Start.Date) return false;
            if (master.RepeatUntil.HasValue && day > master.RepeatUntil.Value.Date) return false;

            int interval = master.RepeatInterval > 0 ? master.RepeatInterval : 1;
            int steps = 0;

            foreach (var start in Starts(master, interval, master.RepeatCount > 0 ? master.Start : day))
            {
                if (++steps > MaxSteps) return false;
                if (start.Date > day) return false;
                if (start.Date == day) return true;
            }
            return false;
        }

        /// <summary>
        /// The start dates the pattern produces, earliest first and unbounded. Every limit -
        /// range, until, count, skips - is applied by the caller, so this stays one job.
        ///
        /// <paramref name="jumpTo"/> is a hint, not a filter: generation begins at the last
        /// pattern date at or before it, so asking for July 2026 of a series that began in 2019
        /// does not walk through seven years of dates first. Callers that need an accurate
        /// running count pass the series start instead and get every date from the beginning.
        /// </summary>
        private static IEnumerable<DateTime> Starts(
            CalendarEvent master, int interval, DateTime jumpTo)
        {
            var first = master.Start;
            if (jumpTo < first) jumpTo = first;

            switch (master.Repeat)
            {
                case RepeatFreq.Daily:
                {
                    long n = (jumpTo.Date - first.Date).Days / interval;
                    if (n < 0) n = 0;
                    for (var d = first.AddDays(n * interval); ; d = d.AddDays(interval))
                        yield return d;
                }

                case RepeatFreq.Weekly:
                {
                    // Which weekdays are ticked. Empty means the day the series starts on, which
                    // is what an untouched weekly repeat should do.
                    var days = new List<DayOfWeek>(master.RepeatDays);
                    if (days.Count == 0) days.Add(first.DayOfWeek);
                    days.Sort();

                    // Whole weeks are walked from the start of the FIRST occurrence's week, so
                    // "every 2 weeks on Mon and Thu" keeps both days inside the same week instead
                    // of the two drifting apart.
                    var firstWeek = first.Date.AddDays(-(int)first.DayOfWeek);
                    var jumpWeek  = jumpTo.Date.AddDays(-(int)jumpTo.DayOfWeek);
                    var time      = first.TimeOfDay;

                    long weeks = (jumpWeek - firstWeek).Days / (7L * interval);
                    if (weeks < 0) weeks = 0;

                    for (var w = firstWeek.AddDays(weeks * 7 * interval); ; w = w.AddDays(7 * interval))
                    {
                        foreach (var dow in days)
                        {
                            var d = w.AddDays((int)dow) + time;
                            if (d < first) continue;   // days earlier in the series' opening week
                            yield return d;
                        }
                    }
                }

                case RepeatFreq.Monthly:
                {
                    // Same day NUMBER each month. A series on the 31st simply has no occurrence in
                    // a 30-day month - it does not slide to the 30th or the 1st, because a user who
                    // picked the 31st did not ask for the 1st. The date is rebuilt from a month
                    // anchor each step, so those skipped months cannot shorten it permanently.
                    int day  = first.Day;
                    var time = first.TimeOfDay;
                    int months = ((jumpTo.Year - first.Year) * 12) + jumpTo.Month - first.Month;
                    int start  = months > 0 ? (months / interval) * interval : 0;

                    for (int i = start; ; i += interval)
                    {
                        var anchor = new DateTime(first.Year, first.Month, 1).AddMonths(i);
                        if (day <= DateTime.DaysInMonth(anchor.Year, anchor.Month))
                            yield return new DateTime(anchor.Year, anchor.Month, day) + time;
                    }
                }

                case RepeatFreq.Yearly:
                {
                    // 29 February exists only in leap years, and is skipped in the others for the
                    // same reason as the 31st above.
                    int month = first.Month, day = first.Day;
                    var time  = first.TimeOfDay;
                    int years = jumpTo.Year - first.Year;
                    int begin = years > 0 ? (years / interval) * interval : 0;

                    for (int i = begin; ; i += interval)
                    {
                        int year = first.Year + i;
                        if (year > 9000) yield break;    // DateTime ceiling, not a real calendar
                        if (day <= DateTime.DaysInMonth(year, month))
                            yield return new DateTime(year, month, day) + time;
                    }
                }

                default:
                    yield break;
            }
        }
    }
}
