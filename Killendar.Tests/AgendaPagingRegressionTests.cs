using System;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    // VE-013: Agenda Prev and Next step by the chosen horizon, so a 7 or 14 day agenda pages
    // without skipping the days between one window and the next.
    public sealed class AgendaPagingRegressionTests
    {
        [Theory]
        [InlineData(7, 1, 2026, 11, 9)]
        [InlineData(7, -1, 2026, 10, 26)]
        [InlineData(14, 1, 2026, 11, 16)]
        [InlineData(14, -1, 2026, 10, 19)]
        [InlineData(60, 1, 2026, 12, 2)]
        [InlineData(60, -1, 2026, 10, 2)]
        [InlineData(365, 1, 2026, 12, 2)]
        public void StepFollowsTheHorizon(int horizon, int direction, int y, int m, int d)
        {
            var monday = new DateTime(2026, 11, 2);
            Assert.Equal(new DateTime(y, m, d), AgendaView.StepBy(monday, direction, horizon));
        }

        // A 30-day page from January 1 shows January 1 to 30. Paging by a whole month would land
        // on February 1 and never show January 31, so it pages by the shorter of the two.
        [Theory]
        [InlineData(2027, 1, 1, 1, 2027, 1, 31)]
        [InlineData(2027, 2, 1, -1, 2027, 1, 2)]
        [InlineData(2027, 2, 1, 1, 2027, 3, 1)]
        public void AThirtyDayPageNeverSkipsADay(int fy, int fm, int fd, int direction, int y, int m, int d)
        {
            Assert.Equal(new DateTime(y, m, d), AgendaView.StepBy(new DateTime(fy, fm, fd), direction, 30));
        }
    }
}
