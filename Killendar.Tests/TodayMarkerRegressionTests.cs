using System;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    // VE-008: Week and Day mark today the way Month does. With nothing selected, or today itself
    // selected, today's column carries the fill; with another day selected it keeps a 1px accent
    // edge instead of losing every marker.
    public sealed class TodayMarkerRegressionTests
    {
        private static readonly DateTime Today = new(2026, 11, 3);

        [Fact]
        public void AnotherSelectedDayShowsTheEdge()
            => Assert.True(TimeGridView.TodayEdgeShown(new DateTime(2026, 11, 5), Today));

        [Fact]
        public void NothingSelectedShowsTheFillInstead()
            => Assert.False(TimeGridView.TodayEdgeShown(null, Today));

        [Fact]
        public void TodaySelectedShowsTheFillInstead()
            => Assert.False(TimeGridView.TodayEdgeShown(Today, Today));
    }
}
