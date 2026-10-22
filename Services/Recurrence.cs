using System;
using System.Collections.Generic;
using Killendar.Models;
using TimeZoneConverter;

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
        /// Occurrences of <paramref name="master"/> overlapping the half-open interval
        /// [from, to). Returns nothing when the master does not repeat: a plain appointment is
        /// not a one-occurrence series, and the caller handles it directly.
        /// </summary>
        public static IEnumerable<CalendarEvent> Expand(
            CalendarEvent master, DateTime from, DateTime to)
        {
            if (master == null || !master.IsSeries) yield break;
            if (master.RepeatShift == 0)
            {
                foreach (var occurrence in ExpandUnshifted(master, from, to)) yield return occurrence;
                yield break;
            }
            // A date on a day off moves to the next workday or the one before, up to two weeks.
            // The dates are read two weeks wider so one moving into the range is found. A moved
            // date keeps its original identity (OccurrenceStart), so editing or skipping it
            // still names the date the pattern gave.
            var reach = TimeSpan.FromDays(ShiftReach);
            var wideFrom = from.Ticks > reach.Ticks ? from - reach : DateTime.MinValue;
            var wideTo = DateTime.MaxValue.Ticks - to.Ticks > reach.Ticks ? to + reach : DateTime.MaxValue;
            // A moved date that lands where the series already falls (a daily repeat's weekend
            // moving onto its own Monday, or Saturday and Sunday both moving there) is one date.
            var taken = new HashSet<DateTime>();
            foreach (var occurrence in ExpandUnshifted(master, wideFrom, wideTo))
            {
                var nominalDay = occurrence.Start.Date;
                var moved = Shifted(occurrence, master);
                if (moved.Start.Date != nominalDay && (taken.Contains(moved.Start.Date) || FallsOn(master, moved.Start.Date)))
                    continue;
                taken.Add(moved.Start.Date);
                if (moved.Start < to && moved.End > from) yield return moved;
            }
        }

        /// <summary>True when the series, unmoved, has a date on <paramref name="day"/> that stays there.</summary>
        private static bool FallsOn(CalendarEvent master, DateTime day)
        {
            if (WorkSchedule.IsDayOff(day)) return false;
            var end = day == DateTime.MaxValue.Date ? DateTime.MaxValue : day.AddDays(1);
            foreach (var occurrence in ExpandUnshifted(master, day, end))
                if (occurrence.Start.Date == day) return true;
            return false;
        }

        /// <summary>Where a date of the series is shown: <paramref name="nominal"/> (the local
        /// start the pattern gives), moved off a day off when the series says so.</summary>
        internal static DateTime ShownStart(CalendarEvent master, DateTime nominal)
        {
            if (master.RepeatShift == 0) return nominal;
            var probe = new CalendarEvent { Start = nominal, End = nominal.AddHours(1), AllDay = master.AllDay };
            return Shifted(probe, master).Start;
        }

        internal const int ShiftReach = 14;

        /// <summary>An occurrence moved off a day off the way <paramref name="master"/> says; unchanged
        /// when two weeks hold no workday. A series kept on another zone's clock moves on that
        /// clock, so it keeps its time of day there across either place's clock change.</summary>
        internal static CalendarEvent Shifted(CalendarEvent occurrence, CalendarEvent master)
        {
            int direction = master.RepeatShift;
            var day = occurrence.Start.Date;
            for (int step = 0; step < ShiftReach && WorkSchedule.IsDayOff(day); step++)
            {
                if ((direction > 0 && day >= DateTime.MaxValue.Date) || (direction < 0 && day <= DateTime.MinValue.Date)) return occurrence;
                day = day.AddDays(Math.Sign(direction));
            }
            if (WorkSchedule.IsDayOff(day) || day == occurrence.Start.Date) return occurrence;
            var delta = day - occurrence.Start.Date;
            try
            {
                if (UsesTimeBasis(master) && !master.AllDay)
                {
                    var length = occurrence.End.ToUniversalTime() - occurrence.Start.ToUniversalTime();
                    var start = FromTimeBasis(master, ToTimeBasis(master, occurrence.Start) + delta).ToLocalTime();
                    occurrence.Start = start;
                    occurrence.End = (start.ToUniversalTime() + length).ToLocalTime();
                }
                else
                {
                    occurrence.Start += delta;
                    occurrence.End += delta;
                }
            }
            catch (ArgumentOutOfRangeException) { }
            return occurrence;
        }

        private static IEnumerable<CalendarEvent> ExpandUnshifted(
            CalendarEvent master, DateTime from, DateTime to)
        {
            if (master.ExtraDates.Count == 0)
            {
                foreach (var occurrence in ExpandRule(master, from, to)) yield return occurrence;
                yield break;
            }
            // The pattern's dates and the extra ones, merged in order; a date both give is one.
            var extras = ExtraOccurrences(master, from, to);
            var seen = new HashSet<long>();
            int next = 0;
            foreach (var occurrence in ExpandRule(master, from, to))
            {
                for (; next < extras.Count && extras[next].Start < occurrence.Start; next++)
                    if (seen.Add(extras[next].Start.Ticks)) yield return extras[next];
                if (seen.Add(occurrence.Start.Ticks)) yield return occurrence;
            }
            for (; next < extras.Count; next++)
                if (seen.Add(extras[next].Start.Ticks)) yield return extras[next];
        }

        /// <summary>The extra dates (RDATE) in range, at the series' time of day on its own clock.
        /// They are not counted toward "ends after N times" and run past its end date, as RFC
        /// 5545 has it; a skipped date skips them too.</summary>
        private static List<CalendarEvent> ExtraOccurrences(CalendarEvent master, DateTime from, DateTime to)
        {
            var list = new List<CalendarEvent>();
            var clock = SeriesClockStart(master);
            var duration = UsesTimeBasis(master) ? master.End.ToUniversalTime() - master.Start.ToUniversalTime() : master.End - master.Start;
            if (duration <= TimeSpan.Zero) duration = master.AllDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
            foreach (var day in master.ExtraDates.Select(d => d.Date).Distinct())
            {
                DateTime nominal, start, end;
                try
                {
                    nominal = master.AllDay ? day : day + clock.TimeOfDay;
                    if (UsesTimeBasis(master) && !master.AllDay)
                    {
                        var utc = FromTimeBasis(master, nominal);
                        start = utc.ToLocalTime();
                        end = NominalDuration.HasValue(master) ? NominalDuration.EndLocal(master, nominal) : (utc + duration).ToLocalTime();
                    }
                    else
                    {
                        start = nominal;
                        end = NominalDuration.HasValue(master) ? NominalDuration.EndLocal(master, start) : start + duration;
                    }
                }
                catch (ArgumentOutOfRangeException) { continue; }
                if (start >= to || end <= from) continue;
                if (master.SkipDates.Exists(d => d.Date == start.Date)) continue;
                if (master.SkipOccurrences.Exists(v => v.Ticks == nominal.Ticks)) continue;
                var occurrence = master.Clone();
                occurrence.Start = start;
                occurrence.End = end;
                occurrence.SeriesId = master.Id;
                occurrence.OccurrenceStart = start;
                SetOccurrenceIdentity(occurrence, master, nominal);
                list.Add(occurrence);
            }
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            return list;
        }

        private static IEnumerable<CalendarEvent> ExpandRule(
            CalendarEvent master, DateTime from, DateTime to)
        {
            if (UsesTimeBasis(master))
            {
                foreach (var occurrence in ExpandTimeBasis(master, from, to)) yield return occurrence;
                yield break;
            }

            var duration = master.End > master.Start
                ? master.End - master.Start
                : (master.AllDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));

            int interval = master.RepeatInterval > 0 ? master.RepeatInterval : 1;

            // Compared by DATE. A skip is the user saying "not that day", and it has to survive
            // the series later being moved to a different time of day.
            var skips = new HashSet<DateTime>();
            foreach (var d in master.SkipDates) skips.Add(d.Date);

            // Include starts before the range that can still overlap it. Pattern ordinals keep
            // COUNT accurate after the jump, including positions consumed by skipped dates.
            var jumpTo = new DateTime(Math.Max(0L, from.Ticks - NominalDuration.LookBackTicks(master, duration.Ticks)), from.Kind);

            foreach (var candidate in Starts(master, interval, jumpTo))
            {
                var start = candidate.Start;

                if (AfterUntil(master, start, start))
                    yield break;

                if (master.RepeatCount > 0 && candidate.Number > master.RepeatCount)
                    yield break;

                if (start >= to) yield break;

                if (skips.Contains(start.Date)) continue;
                if (master.SkipOccurrences.Exists(value => value.Ticks == start.Ticks)) continue;

                if (duration.Ticks > DateTime.MaxValue.Ticks - start.Ticks) yield break;
                DateTime end;
                try { end = NominalDuration.HasValue(master) ? NominalDuration.EndLocal(master, start) : start + duration; }
                catch (ArgumentOutOfRangeException) { yield break; }
                if (end <= from) continue;

                var occ = master.Clone();
                occ.Start           = start;
                occ.End             = end;
                occ.SeriesId        = master.Id;
                occ.OccurrenceStart = start;
                SetOccurrenceIdentity(occ, master, start);
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
            if (master.ExtraDates.Count > 0)
            {
                var day0 = candidate.Date;
                var probe = master.Clone();
                probe.SkipDates = [];
                probe.SkipOccurrences = [];
                var end0 = day0 == DateTime.MaxValue.Date ? DateTime.MaxValue : day0.AddDays(1);
                if (ExtraOccurrences(probe, day0, end0).Exists(o => o.Start.Date == day0)) return true;
            }
            if (UsesTimeBasis(master))
            {
                var pattern = master.Clone();
                pattern.SkipDates.Clear();
                pattern.SkipOccurrences.Clear();
                var end = candidate.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : candidate.Date.AddDays(1);
                foreach (var occurrence in ExpandTimeBasis(pattern, candidate.Date, end))
                    if (occurrence.Start.Date == candidate.Date) return true;
                return false;
            }

            var day = candidate.Date;
            if (day < master.Start.Date) return false;
            if (!master.RepeatUntilExact.HasValue && master.RepeatUntil.HasValue && day > master.RepeatUntil.Value.Date) return false;

            int interval = master.RepeatInterval > 0 ? master.RepeatInterval : 1;

            foreach (var occurrence in Starts(master, interval, day))
            {
                var start = occurrence.Start;
                if (master.RepeatCount > 0 && occurrence.Number > master.RepeatCount) return false;
                if (start.Date > day) return false;
                if (start.Date == day) return !AfterUntil(master, start, start);
            }
            return false;
        }

        /// <summary>The <paramref name="week"/>th (1 to 4) or last (-1) <paramref name="day"/> of a month.</summary>
        internal static DateTime NthWeekday(int year, int month, DayOfWeek day, int week)
        {
            if (week < 0)
            {
                var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                return last.AddDays(-(((int)last.DayOfWeek - (int)day + 7) % 7));
            }
            var first = new DateTime(year, month, 1);
            return first.AddDays(((int)day - (int)first.DayOfWeek + 7) % 7 + 7 * (week - 1));
        }

        /// <summary>The start on the series' own clock: the source zone's (or UTC) for a series
        /// kept on one, else the local start. The weekday rules are read on this clock.</summary>
        internal static DateTime SeriesClockStart(CalendarEvent ev) =>
            UsesTimeBasis(ev) ? ev.RepeatAnchor!.Value : ev.Start;

        /// <summary>
        /// A monthly weekday choice that still fits <paramref name="start"/>. A start moved to
        /// another week keeps "a weekday of the month" and follows the new week; the last one stays
        /// last while the start is in the month's last week, and a start in the fifth week is the
        /// last. 0 (same date each month) is left alone.
        /// </summary>
        internal static int FitMonthWeek(DateTime start, int week)
        {
            if (week == 0) return 0;
            if (week == -1 && IsLastWeekdayOfMonth(start)) return -1;
            int now = WeekOfMonth(start);
            return now <= 4 ? now : -1;
        }

        /// <summary>Which week of its month a date's weekday falls in: 1 to 5.</summary>
        internal static int WeekOfMonth(DateTime date) => (date.Day - 1) / 7 + 1;

        /// <summary>True when <paramref name="date"/> is the last of its weekday in its month.</summary>
        internal static bool IsLastWeekdayOfMonth(DateTime date) => date.Day + 7 > DateTime.DaysInMonth(date.Year, date.Month);

        internal static bool UsesTimeBasis(CalendarEvent master) => !master.AllDay &&
            master.RepeatAnchor.HasValue && (master.RepeatTimeBasis == RecurrenceTimeBasis.Utc ||
            master.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone);

        internal static bool AfterUntil(CalendarEvent master, DateTime nominal, DateTime local)
        {
            if (master.RepeatUntilExact.HasValue)
                return master.RepeatUntilExact.Value.Kind == DateTimeKind.Utc
                    ? local.ToUniversalTime() > master.RepeatUntilExact.Value
                    : nominal.Ticks > master.RepeatUntilExact.Value.Ticks;
            return master.RepeatUntil.HasValue && local.Date > master.RepeatUntil.Value.Date;
        }

        internal static void SetOccurrenceIdentity(CalendarEvent row, CalendarEvent master, DateTime nominal)
        {
            row.OccurrenceAnchor = DateTime.SpecifyKind(master.AllDay ? nominal.Date : nominal, DateTimeKind.Unspecified);
            row.OccurrenceBasis = master.AllDay ? OccurrenceTimeBasis.Date : master.RepeatTimeBasis == RecurrenceTimeBasis.Utc
                ? OccurrenceTimeBasis.Utc : master.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone
                    ? OccurrenceTimeBasis.SourceZone : OccurrenceTimeBasis.Floating;
            row.OccurrenceZoneId = row.OccurrenceBasis == OccurrenceTimeBasis.SourceZone ? master.RepeatZoneId : "";
            row.OccurrenceStart = master.AllDay ? nominal.Date : UsesTimeBasis(master)
                ? FromTimeBasis(master, nominal).ToLocalTime() : DateTime.SpecifyKind(nominal, DateTimeKind.Local);
        }

        internal static DateTime OriginalInstant(CalendarEvent row)
        {
            if (!row.OccurrenceAnchor.HasValue) return (row.OccurrenceStart ?? row.Start).ToUniversalTime();
            var anchor = row.OccurrenceAnchor.Value;
            if (row.OccurrenceBasis == OccurrenceTimeBasis.Utc) return DateTime.SpecifyKind(anchor, DateTimeKind.Utc);
            if (row.OccurrenceBasis == OccurrenceTimeBasis.SourceZone)
                return FromTimeBasis(new CalendarEvent { RepeatTimeBasis = RecurrenceTimeBasis.SourceZone, RepeatZoneId = row.OccurrenceZoneId }, anchor);
            return DateTime.SpecifyKind(anchor, DateTimeKind.Local).ToUniversalTime();
        }

        internal static long OccurrenceKey(CalendarEvent master, CalendarEvent row) => master.AllDay
            ? (row.OccurrenceAnchor.HasValue && row.OccurrenceBasis == OccurrenceTimeBasis.Date
                ? row.OccurrenceAnchor.Value.Date : (row.OccurrenceStart ?? row.Start).Date).Ticks
            : OriginalInstant(row).Ticks;

        internal static DateTime CanonicalOccurrenceStart(CalendarEvent master, CalendarEvent row)
        {
            if (row.OccurrenceAnchor.HasValue &&
                ((master.AllDay && row.OccurrenceBasis == OccurrenceTimeBasis.Date) ||
                 (!master.AllDay && master.RepeatTimeBasis == RecurrenceTimeBasis.Local && row.OccurrenceBasis == OccurrenceTimeBasis.Floating) ||
                 (!master.AllDay && master.RepeatTimeBasis == RecurrenceTimeBasis.Utc && row.OccurrenceBasis == OccurrenceTimeBasis.Utc) ||
                 (!master.AllDay && master.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone && row.OccurrenceBasis == OccurrenceTimeBasis.SourceZone && master.RepeatZoneId == row.OccurrenceZoneId)))
                return row.OccurrenceAnchor.Value;
            var local = OriginalInstant(row).ToLocalTime();
            if (master.AllDay) return local.Date;
            return UsesTimeBasis(master) ? OriginalTimeBasisStart(master, local) : DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        }

        internal static bool GeneratesCanonicalStart(CalendarEvent master, DateTime nominal)
        {
            var pattern = master.Clone();
            if (UsesTimeBasis(master)) pattern.Start = master.RepeatAnchor!.Value;
            int interval = master.RepeatInterval > 0 ? master.RepeatInterval : 1;
            foreach (var candidate in UsesTimeBasis(master)
                ? TimeBasisStarts(master, nominal) : Starts(pattern, interval, nominal))
            {
                if (candidate.Start.Ticks > nominal.Ticks || (master.RepeatCount > 0 && candidate.Number > master.RepeatCount)) return false;
                if (candidate.Start.Ticks == nominal.Ticks)
                {
                    var local = UsesTimeBasis(master) ? FromTimeBasis(master, nominal).ToLocalTime() : nominal;
                    return !AfterUntil(master, nominal, local);
                }
            }
            return false;
        }

        internal static DateTime ToTimeBasis(CalendarEvent master, DateTime local)
        {
            var utc = local.ToUniversalTime();
            return DateTime.SpecifyKind(master.RepeatTimeBasis == RecurrenceTimeBasis.Utc ? utc :
                TimeZoneInfo.ConvertTimeFromUtc(utc, TZConvert.GetTimeZoneInfo(master.RepeatZoneId)),
                DateTimeKind.Unspecified);
        }

        internal static DateTime FromTimeBasis(CalendarEvent master, DateTime source)
        {
            source = DateTime.SpecifyKind(source, DateTimeKind.Unspecified);
            if (master.RepeatTimeBasis == RecurrenceTimeBasis.Utc)
                return DateTime.SpecifyKind(source, DateTimeKind.Utc);
            var zone = TZConvert.GetTimeZoneInfo(master.RepeatZoneId);
            if (zone.IsAmbiguousTime(source))
            {
                var offsets = zone.GetAmbiguousTimeOffsets(source);
                return DateTime.SpecifyKind(source - (offsets[0] > offsets[1] ? offsets[0] : offsets[1]), DateTimeKind.Utc);
            }
            if (zone.IsInvalidTime(source))
                return DateTime.SpecifyKind(source - zone.GetUtcOffset(source.AddHours(-12)), DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeToUtc(source, zone);
        }

        internal static DateTime OriginalTimeBasisStart(CalendarEvent master, DateTime occurrenceStart)
        {
            var approximate = ToTimeBasis(master, occurrenceStart);
            var expectedUtc = occurrenceStart.ToUniversalTime();
            long margin = TimeSpan.FromDays(2).Ticks;
            var from = new DateTime(Math.Max(0, approximate.Ticks - margin));
            var to = new DateTime(approximate.Ticks + Math.Min(margin, DateTime.MaxValue.Ticks - approximate.Ticks));
            foreach (var candidate in TimeBasisStarts(master, from))
            {
                if (candidate.Start > to || (master.RepeatCount > 0 && candidate.Number > master.RepeatCount)) break;
                DateTime utc;
                try { utc = FromTimeBasis(master, candidate.Start); }
                catch (ArgumentOutOfRangeException) { continue; }
                if (utc == expectedUtc) return candidate.Start;
            }
            throw new InvalidOperationException("The original recurrence occurrence could not be located in its source time basis.");
        }

        private static IEnumerable<CalendarEvent> ExpandTimeBasis(CalendarEvent master, DateTime from, DateTime to)
        {
            var duration = master.End.ToUniversalTime() - master.Start.ToUniversalTime();
            if (duration <= TimeSpan.Zero) duration = TimeSpan.FromHours(1);
            // Source and display offsets can differ by up to 28 hours. Padding the jump and
            // stop bounds keeps every candidate; actual overlap is checked after conversion.
            long margin = TimeSpan.FromDays(2).Ticks;
            long earliestOverlap = Math.Max(0, from.Ticks - NominalDuration.LookBackTicks(master, duration.Ticks));
            var jump = new DateTime(Math.Max(0, earliestOverlap - margin));
            var stop = new DateTime(to.Ticks + Math.Min(margin, DateTime.MaxValue.Ticks - to.Ticks));
            var skips = new HashSet<DateTime>();
            foreach (var day in master.SkipDates) skips.Add(day.Date);
            foreach (var candidate in TimeBasisStarts(master, jump))
            {
                if (candidate.Start >= stop || (master.RepeatCount > 0 && candidate.Number > master.RepeatCount)) yield break;
                DateTime utc;
                DateTime endUtc;
                try
                {
                    utc = FromTimeBasis(master, candidate.Start);
                    endUtc = NominalDuration.HasValue(master)
                        ? NominalDuration.EndLocal(master, candidate.Start).ToUniversalTime() : utc + duration;
                }
                catch (ArgumentOutOfRangeException) { continue; }
                var start = utc.ToLocalTime();
                var end = endUtc.ToLocalTime();
                if (AfterUntil(master, candidate.Start, start)) continue;
                if (skips.Contains(start.Date) || start >= to || end <= from) continue;
                if (master.SkipOccurrences.Exists(value => value.Ticks == candidate.Start.Ticks)) continue;
                var occurrence = master.Clone();
                occurrence.Start = start;
                occurrence.End = end;
                occurrence.SeriesId = master.Id;
                occurrence.OccurrenceStart = start;
                SetOccurrenceIdentity(occurrence, master, candidate.Start);
                yield return occurrence;
            }
        }

        private static IEnumerable<(DateTime Start, long Number)> TimeBasisStarts(CalendarEvent master, DateTime jumpTo)
        {
            var pattern = master.Clone();
            pattern.Start = master.RepeatAnchor!.Value;
            int interval = master.RepeatInterval > 0 ? master.RepeatInterval : 1;
            var zone = master.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone
                ? TZConvert.GetTimeZoneInfo(master.RepeatZoneId) : null;
            bool countValid = zone != null && master.RepeatCount > 0;
            long number = 0;
            // COUNT applies to valid local times, so a range jump must include earlier gaps
            // in its accounting. Explicit DTSTART keeps its separate gap interpretation.
            foreach (var candidate in Starts(pattern, interval, countValid ? pattern.Start : jumpTo))
            {
                if (zone != null && candidate.Start.Ticks != pattern.Start.Ticks &&
                    zone.IsInvalidTime(DateTime.SpecifyKind(candidate.Start, DateTimeKind.Unspecified))) continue;
                yield return (candidate.Start, countValid ? ++number : candidate.Number);
            }
        }

        /// <summary>
        /// The start dates and one-based ordinals the pattern produces, earliest first within
        /// DateTime bounds. Every limit -
        /// range, until, count, skips - is applied by the caller, so this stays one job.
        ///
        /// <paramref name="jumpTo"/> is a hint, not a filter: generation begins at the last
        /// pattern date at or before it, so asking for July 2026 of a series that began in 2019
        /// does not walk through seven years of dates first. Callers that need an accurate
        /// running count use the ordinal, which includes valid pattern dates before the jump.
        /// </summary>
        private static IEnumerable<(DateTime Start, long Number)> Starts(
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
                    long lastDay = (DateTime.MaxValue.Ticks - first.Ticks) / TimeSpan.TicksPerDay;
                    for (long day = n * interval; day <= lastDay; day += interval)
                        yield return (first.AddDays(day), day / interval + 1);
                    yield break;
                }

                case RepeatFreq.Weekly:
                {
                    // Which weekdays are ticked. Empty means the day the series starts on, which
                    // is what an untouched weekly repeat should do.
                    var days = new List<DayOfWeek>(master.RepeatDays);
                    if (days.Count == 0) days.Add(first.DayOfWeek);
                    int Offset(DayOfWeek day) => ((int)day - (int)master.RepeatWeekStart + 7) % 7;
                    days.Sort((left, right) => Offset(left).CompareTo(Offset(right)));

                    // Whole weeks are walked from the start of the FIRST occurrence's week, so
                    // "every 2 weeks on Mon and Thu" keeps both days inside the same week instead
                    // of the two drifting apart.
                    long firstWeek = first.Date.Ticks / TimeSpan.TicksPerDay - Offset(first.DayOfWeek);
                    long jumpWeek = jumpTo.Date.Ticks / TimeSpan.TicksPerDay - Offset(jumpTo.DayOfWeek);
                    var time      = first.TimeOfDay;

                    long weeks = (jumpWeek - firstWeek) / (7L * interval);
                    if (weeks < 0) weeks = 0;
                    long number = weeks * days.Count;
                    if (weeks > 0)
                        foreach (var dow in days)
                            if (Offset(dow) < Offset(first.DayOfWeek)) number--;

                    long lastDay = DateTime.MaxValue.Ticks / TimeSpan.TicksPerDay;
                    for (long w = firstWeek + weeks * 7L * interval; w <= lastDay; w += 7L * interval)
                    {
                        foreach (var dow in days)
                        {
                            long day = w + Offset(dow);
                            if (day < 0) continue;
                            if (day > lastDay) yield break;
                            var d = new DateTime(day * TimeSpan.TicksPerDay + time.Ticks, first.Kind);
                            if (d < first) continue;   // days earlier in the series' opening week
                            yield return (d, ++number);
                        }
                    }
                    yield break;
                }

                case RepeatFreq.Monthly when master.RepeatMonthWeek != 0:
                {
                    // The Nth (or last) weekday of each month. Every month has a first to fourth
                    // and a last of each weekday, so every month counts.
                    var time = first.TimeOfDay;
                    int months = ((jumpTo.Year - first.Year) * 12) + jumpTo.Month - first.Month;
                    int start  = months > 0 ? (months / interval) * interval : 0;
                    int lastMonth = (9999 - first.Year) * 12 + 12 - first.Month;
                    for (long i = start; i <= lastMonth; i += interval)
                    {
                        var anchor = new DateTime(first.Year, first.Month, 1).AddMonths((int)i);
                        var day = NthWeekday(anchor.Year, anchor.Month, first.DayOfWeek, master.RepeatMonthWeek);
                        if (day + time < first) continue;
                        yield return (day + time, i / interval + 1);
                    }
                    yield break;
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

                    int lastMonth = (9999 - first.Year) * 12 + 12 - first.Month;
                    long number = 0;
                    for (long i = master.RepeatCount > 0 ? 0 : start; i <= lastMonth; i += interval)
                    {
                        var anchor = new DateTime(first.Year, first.Month, 1).AddMonths((int)i);
                        if (day <= DateTime.DaysInMonth(anchor.Year, anchor.Month))
                        {
                            number++;
                            if (i >= start)
                                yield return (new DateTime(anchor.Year, anchor.Month, day) + time, number);
                        }
                    }
                    yield break;
                }

                case RepeatFreq.Yearly:
                {
                    // 29 February exists only in leap years, and is skipped in the others for the
                    // same reason as the 31st above.
                    int month = first.Month, day = first.Day;
                    var time  = first.TimeOfDay;
                    int years = jumpTo.Year - first.Year;
                    int begin = years > 0 ? (years / interval) * interval : 0;

                    long number = 0;
                    for (long i = master.RepeatCount > 0 ? 0 : begin; i <= 9999 - first.Year; i += interval)
                    {
                        int year = first.Year + (int)i;
                        if (day <= DateTime.DaysInMonth(year, month))
                        {
                            number++;
                            if (i >= begin)
                                yield return (new DateTime(year, month, day) + time, number);
                        }
                    }
                    yield break;
                }

                default:
                    yield break;
            }
        }
    }
}
