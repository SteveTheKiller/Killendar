using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class RecurrenceCountRegressionTests
    {
        [Theory]
        [InlineData(2014, 10, 4)]
        [InlineData(2026, 1, 1)]
        [InlineData(2028, 6, 11)]
        public void LongDailyCountStillIncludesOccurrencesAfterTwentyThousandSteps(int year, int month, int day)
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(1960, 1, 1),
                End = new DateTime(1960, 1, 1, 1, 0, 0),
                Repeat = RepeatFreq.Daily,
                RepeatCount = 25000
            };
            var from = new DateTime(year, month, day);

            var occurrence = Assert.Single(Recurrence.Expand(master, from, from.AddDays(1)));

            Assert.Equal(from, occurrence.Start);
            Assert.True(Recurrence.GeneratesDate(master, from));
            Assert.Empty(Recurrence.Expand(master, new DateTime(2028, 6, 12), new DateTime(2028, 6, 13)));
            Assert.False(Recurrence.GeneratesDate(master, new DateTime(2028, 6, 12)));
        }

        [Fact]
        public void GeneratesDateRejectsTheFirstDateAfterCount()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 1),
                End = new DateTime(2026, 1, 1, 1, 0, 0),
                Repeat = RepeatFreq.Daily,
                RepeatCount = 2
            };

            Assert.False(Recurrence.GeneratesDate(master, new DateTime(2026, 1, 3)));
        }

        [Fact]
        public void WeeklyCountPreservesThePartialOpeningWeekAndSkippedDate()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 7, 9, 0, 0),
                End = new DateTime(2026, 1, 7, 10, 0, 0),
                Repeat = RepeatFreq.Weekly,
                RepeatCount = 4,
                RepeatInterval = 2
            };
            master.RepeatDays.Add(DayOfWeek.Monday);
            master.RepeatDays.Add(DayOfWeek.Thursday);
            master.SkipDates.Add(new DateTime(2026, 1, 19));

            var starts = Recurrence.Expand(master, new DateTime(2026, 1, 19), new DateTime(2026, 2, 6))
                .Select(occurrence => occurrence.Start).ToArray();

            Assert.Equal(new[] { new DateTime(2026, 1, 22, 9, 0, 0), new DateTime(2026, 2, 2, 9, 0, 0) }, starts);
            Assert.True(Recurrence.GeneratesDate(master, new DateTime(2026, 2, 2)));
            Assert.False(Recurrence.GeneratesDate(master, new DateTime(2026, 2, 5)));
        }

        [Fact]
        public void MonthlyCountCountsOnlyExistingThirtyFirsts()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 31),
                End = new DateTime(2026, 1, 31, 1, 0, 0),
                Repeat = RepeatFreq.Monthly,
                RepeatCount = 3
            };
            master.SkipDates.Add(new DateTime(2026, 3, 31));

            var starts = Recurrence.Expand(master, new DateTime(2026, 3, 1), new DateTime(2026, 8, 1))
                .Select(occurrence => occurrence.Start).ToArray();

            Assert.Equal(new[] { new DateTime(2026, 5, 31) }, starts);
            Assert.True(Recurrence.GeneratesDate(master, new DateTime(2026, 5, 31)));
            Assert.False(Recurrence.GeneratesDate(master, new DateTime(2026, 7, 31)));
        }

        [Fact]
        public void YearlyCountCountsOnlyLeapDays()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(2024, 2, 29),
                End = new DateTime(2024, 2, 29, 1, 0, 0),
                Repeat = RepeatFreq.Yearly,
                RepeatCount = 3
            };
            master.SkipDates.Add(new DateTime(2028, 2, 29));

            var starts = Recurrence.Expand(master, new DateTime(2025, 1, 1), new DateTime(2037, 1, 1))
                .Select(occurrence => occurrence.Start).ToArray();

            Assert.Equal(new[] { new DateTime(2032, 2, 29) }, starts);
            Assert.True(Recurrence.GeneratesDate(master, new DateTime(2032, 2, 29)));
            Assert.False(Recurrence.GeneratesDate(master, new DateTime(2036, 2, 29)));
        }

        [Fact]
        public void FullCountedExpansionDoesNotSilentlyCapItsOutput()
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(1960, 1, 1),
                End = new DateTime(1960, 1, 1, 1, 0, 0),
                Repeat = RepeatFreq.Daily,
                RepeatCount = 25000
            };

            Assert.Equal(25000, Recurrence.Expand(master, new DateTime(1960, 1, 1), new DateTime(2030, 1, 1)).Count());
        }
    }
}
