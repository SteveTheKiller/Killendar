using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class RecurrenceTests
    {
        [Fact]
        public void MonthlyThirtyFirstSkipsShortMonths()
        {
            var series = new CalendarEvent
            {
                Start = new DateTime(2026, 1, 31, 9, 0, 0),
                End = new DateTime(2026, 1, 31, 10, 0, 0),
                Repeat = RepeatFreq.Monthly,
                RepeatInterval = 1,
            };

            var dates = Recurrence.Expand(series,
                    new DateTime(2026, 1, 1), new DateTime(2026, 4, 1))
                .Select(e => e.Start.Date)
                .ToArray();

            Assert.Equal(new[] { new DateTime(2026, 1, 31), new DateTime(2026, 3, 31) }, dates);
        }

        [Fact]
        public void LeapDayYearlySeriesOnlyOccursInLeapYears()
        {
            var series = new CalendarEvent
            {
                Start = new DateTime(2024, 2, 29, 9, 0, 0),
                End = new DateTime(2024, 2, 29, 10, 0, 0),
                Repeat = RepeatFreq.Yearly,
                RepeatInterval = 1,
            };

            var dates = Recurrence.Expand(series,
                    new DateTime(2025, 1, 1), new DateTime(2029, 1, 1))
                .Select(e => e.Start.Date)
                .ToArray();

            Assert.Equal(new[] { new DateTime(2028, 2, 29) }, dates);
        }
    }
}
