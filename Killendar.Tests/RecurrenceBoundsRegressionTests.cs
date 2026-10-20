using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class RecurrenceBoundsRegressionTests
    {
        [Theory]
        [InlineData(RepeatFreq.Daily)]
        [InlineData(RepeatFreq.Weekly)]
        [InlineData(RepeatFreq.Monthly)]
        [InlineData(RepeatFreq.Yearly)]
        public void MaxIntervalStopsAfterItsOnlyRepresentableOccurrence(RepeatFreq frequency)
        {
            var start = new DateTime(2026, 10, 20, 9, 0, 0);
            var master = new CalendarEvent
            {
                Start = start,
                End = start.AddHours(1),
                Repeat = frequency,
                RepeatInterval = int.MaxValue
            };

            var occurrences = Recurrence.Expand(master, start.Date, start.Date.AddDays(1)).ToArray();

            Assert.Equal(start, Assert.Single(occurrences).Start);
            Assert.False(Recurrence.GeneratesDate(master, new DateTime(9999, 12, 31)));
        }

        [Theory]
        [InlineData(RepeatFreq.Daily, 1)]
        [InlineData(RepeatFreq.Weekly, 1)]
        [InlineData(RepeatFreq.Monthly, 1)]
        [InlineData(RepeatFreq.Yearly, 1)]
        [InlineData(RepeatFreq.Daily, 9999)]
        [InlineData(RepeatFreq.Weekly, 9999)]
        [InlineData(RepeatFreq.Monthly, 9999)]
        [InlineData(RepeatFreq.Yearly, 9999)]
        public void BoundaryStartsRemainRepresentable(RepeatFreq frequency, int year)
        {
            var start = year == 1 ? new DateTime(1, 1, 1, 9, 0, 0) : new DateTime(9999, 12, 31, 9, 0, 0);
            foreach (int interval in new[] { 1, int.MaxValue })
            {
                var master = new CalendarEvent
                {
                    Start = start,
                    End = start.AddHours(1),
                    Repeat = frequency,
                    RepeatInterval = interval
                };

                var occurrences = Recurrence.Expand(master, start.Date, start.AddHours(2)).ToArray();
                Assert.Equal(start, Assert.Single(occurrences).Start);
                Assert.True(Recurrence.GeneratesDate(master, start));
            }
        }

        [Theory]
        [InlineData(RepeatFreq.Daily)]
        [InlineData(RepeatFreq.Weekly)]
        [InlineData(RepeatFreq.Monthly)]
        [InlineData(RepeatFreq.Yearly)]
        public void OccurrenceWhoseEndCannotBeRepresentedIsNotEmitted(RepeatFreq frequency)
        {
            var master = new CalendarEvent
            {
                Start = new DateTime(9998, 12, 31, 23, 0, 0),
                End = new DateTime(9999, 1, 1, 1, 0, 0),
                Repeat = frequency
            };
            master.RepeatDays.Add(DayOfWeek.Friday);

            Assert.Empty(Recurrence.Expand(master, new DateTime(9999, 12, 31, 23, 0, 0), DateTime.MaxValue));
        }
    }
}
