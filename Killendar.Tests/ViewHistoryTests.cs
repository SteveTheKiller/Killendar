using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Back and forward: the views and dates visited, like a browser's history.
    public sealed class ViewHistoryTests
    {
        private static readonly ViewHistory.Spot Year = new("Year", new DateTime(2026, 1, 1));
        private static readonly ViewHistory.Spot Day = new("Day", new DateTime(2026, 3, 4));
        private static readonly ViewHistory.Spot Week = new("Week", new DateTime(2026, 3, 4));

        [Fact]
        public void BackReturnsWhereYouCameFrom()
        {
            var h = new ViewHistory();
            h.Visit(Year);
            Assert.Equal(Year, h.Back(Day));
            Assert.False(h.CanGoBack);
        }

        [Fact]
        public void ForwardUndoesBack()
        {
            var h = new ViewHistory();
            h.Visit(Year);
            h.Visit(Day);
            var back = h.Back(Week);
            Assert.Equal(Day, back);
            Assert.Equal(Week, h.Forward(Day));
            Assert.False(h.CanGoForward);
        }

        [Fact]
        public void ANewVisitClearsForward()
        {
            var h = new ViewHistory();
            h.Visit(Year);
            h.Back(Day);
            h.Visit(Year);
            Assert.False(h.CanGoForward);
            Assert.Null(h.Forward(Week));
        }

        [Fact]
        public void BackWithNothingRecordedDoesNothing()
            => Assert.Null(new ViewHistory().Back(Day));

        [Fact]
        public void HistoryKeepsOnlyTheNewestSteps()
        {
            var h = new ViewHistory();
            for (int i = 0; i < ViewHistory.MaxSteps + 10; i++)
                h.Visit(new ViewHistory.Spot("Day", new DateTime(2026, 1, 1).AddDays(i)));
            int steps = 0;
            var here = Day;
            while (h.Back(here) is ViewHistory.Spot s) { here = s; steps++; }
            Assert.Equal(ViewHistory.MaxSteps, steps);
            Assert.Equal(new DateTime(2026, 1, 1).AddDays(10), here.Anchor);
        }
    }
}
