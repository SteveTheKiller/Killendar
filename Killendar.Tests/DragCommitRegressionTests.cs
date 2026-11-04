using System.Windows;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    // VE-007: a chip released off the calendar surface snaps home instead of committing to the
    // nearest edge cell, and Escape cancels a drag in flight.
    public sealed class DragCommitRegressionTests
    {
        // A Week body 700 wide and 500 tall with a 46px time gutter on its left.
        private static readonly Rect Week = new(46, 0, 654, 500);

        [Fact]
        public void ReleaseOverADayColumnCommits()
            => Assert.True(DragCommit.Lands(new Point(300, 250), Week));

        [Theory]
        [InlineData(20, 250)]     // over the time gutter
        [InlineData(300, -12)]    // above the grid, over the day headers
        [InlineData(300, 520)]    // below the grid
        [InlineData(760, 250)]    // right of the grid, over the sidebar
        [InlineData(-40, 250)]    // left of the window's calendar area
        public void ReleaseOffTheGridDoesNotCommit(double x, double y)
            => Assert.False(DragCommit.Lands(new Point(x, y), Week));

        [Fact]
        public void AnUnlaidSurfaceNeverCommits()
            => Assert.False(DragCommit.Lands(new Point(0, 0), new Rect(0, 0, 0, 0)));

        [Fact]
        public void EscapeCancelsTheDragInFlightOnce()
        {
            int cancels = 0;
            void Cancel() => cancels++;
            DragCommit.Begin(Cancel);
            try
            {
                Assert.True(DragCommit.TryCancel());
                Assert.Equal(1, cancels);
                Assert.False(DragCommit.TryCancel());
                Assert.Equal(1, cancels);
            }
            finally { DragCommit.End(Cancel); }
        }

        [Fact]
        public void EscapeAfterTheDropHasNothingToCancel()
        {
            int cancels = 0;
            void Cancel() => cancels++;
            DragCommit.Begin(Cancel);
            DragCommit.End(Cancel);
            Assert.False(DragCommit.TryCancel());
            Assert.Equal(0, cancels);
        }
    }
}
