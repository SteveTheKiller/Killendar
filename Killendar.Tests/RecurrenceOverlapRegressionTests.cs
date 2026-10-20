using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class RecurrenceOverlapRegressionTests
    {
        [Fact]
        public void DailyRangeIncludesEveryEarlierOccurrenceThatStillOverlaps()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 1),
                End = new DateTime(2026, 1, 4),
                Repeat = RepeatFreq.Daily
            };

            var starts = Recurrence.Expand(master, new DateTime(2026, 1, 3), new DateTime(2026, 1, 4))
                .Select(occurrence => occurrence.Start).ToArray();

            Assert.Equal(new[] { new DateTime(2026, 1, 1), new DateTime(2026, 1, 2), new DateTime(2026, 1, 3) }, starts);
        }

        [Fact]
        public void WeeklyRangeIncludesEarlierWeeksStillInProgress()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 1),
                End = new DateTime(2026, 1, 22),
                Repeat = RepeatFreq.Weekly
            };

            var starts = Recurrence.Expand(master, new DateTime(2026, 1, 15), new DateTime(2026, 1, 16))
                .Select(occurrence => occurrence.Start).ToArray();

            Assert.Equal(new[] { new DateTime(2026, 1, 1), new DateTime(2026, 1, 8), new DateTime(2026, 1, 15) }, starts);
        }

        [Fact]
        public void MonthlyRangeIncludesEarlierMonthsStillInProgress()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 1),
                End = new DateTime(2026, 4, 1),
                Repeat = RepeatFreq.Monthly
            };

            var starts = Recurrence.Expand(master, new DateTime(2026, 3, 1), new DateTime(2026, 3, 2))
                .Select(occurrence => occurrence.Start).ToArray();

            Assert.Equal(new[] { new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), new DateTime(2026, 3, 1) }, starts);
        }

        [Fact]
        public void RangeExcludesOccurrencesEndingAtFromOrStartingAtTo()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 1),
                End = new DateTime(2026, 1, 4),
                Repeat = RepeatFreq.Daily
            };

            var starts = Recurrence.Expand(master, new DateTime(2026, 1, 4), new DateTime(2026, 1, 5))
                .Select(occurrence => occurrence.Start).ToArray();

            Assert.Equal(new[] { new DateTime(2026, 1, 2), new DateTime(2026, 1, 3), new DateTime(2026, 1, 4) }, starts);
        }

        [Fact]
        public void EarlierOverlapsStillRespectCountAndSkippedDates()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 1),
                End = new DateTime(2026, 1, 4),
                Repeat = RepeatFreq.Daily,
                RepeatCount = 2
            };
            master.SkipDates.Add(new DateTime(2026, 1, 2));

            var occurrences = Recurrence.Expand(master, new DateTime(2026, 1, 3), new DateTime(2026, 1, 4)).ToArray();

            Assert.Equal(new DateTime(2026, 1, 1), Assert.Single(occurrences).Start);
        }

        [Fact]
        public void CandidateFloorClampsAtTheFirstRepresentableDate()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(1, 1, 1),
                End = new DateTime(1, 1, 4),
                Repeat = RepeatFreq.Daily
            };

            var starts = Recurrence.Expand(master, new DateTime(1, 1, 2), new DateTime(1, 1, 3))
                .Select(occurrence => occurrence.Start).ToArray();

            Assert.Equal(new[] { new DateTime(1, 1, 1), new DateTime(1, 1, 2) }, starts);
        }
    }
}
