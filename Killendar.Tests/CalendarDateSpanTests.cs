using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class CalendarDateSpanTests
    {
        [Fact]
        public void StoredAllDayEndDisplaysAsLastCoveredDate()
        {
            var start = new DateTime(2026, 8, 16);
            Assert.Equal(new DateTime(2026, 8, 18),
                CalendarDateSpan.VisibleAllDayEnd(start, new DateTime(2026, 8, 19)));
        }

        [Fact]
        public void DisplayedAllDayEndReturnsToExclusiveStorageBoundary()
        {
            var start = new DateTime(2026, 8, 16);
            var displayed = CalendarDateSpan.VisibleAllDayEnd(start, new DateTime(2026, 8, 19));
            Assert.Equal(new DateTime(2026, 8, 19),
                CalendarDateSpan.ExclusiveAllDayEnd(start, displayed));
        }

        [Fact]
        public void EndBeforeStartStillProducesOneDayEvent()
        {
            var start = new DateTime(2026, 8, 16);
            Assert.Equal(start.AddDays(1),
                CalendarDateSpan.ExclusiveAllDayEnd(start, start.AddDays(-2)));
        }
    }
}
