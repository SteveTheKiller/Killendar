using System;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    public sealed class DragDropMathRegressionTests
    {
        [Fact]
        public void TimeGrid_DraggingOvernightContinuationMovesTheRealStart()
        {
            var start = new DateTime(2026, 3, 2, 22, 0, 0);
            var grab = new DateTime(2026, 3, 3, 0, 15, 0);
            var drop = new DateTime(2026, 3, 3, 0, 45, 0);
            Assert.Equal(new DateTime(2026, 3, 2, 22, 30, 0), TimeGridView.DroppedStart(start, grab, drop, 15));
        }

        [Fact]
        public void TimeGrid_SingleDayEventKeepsGrabOffset()
        {
            var start = new DateTime(2026, 3, 2, 9, 0, 0);
            var grab = new DateTime(2026, 3, 2, 9, 40, 0);
            var drop = new DateTime(2026, 3, 4, 11, 40, 0);
            Assert.Equal(new DateTime(2026, 3, 4, 11, 0, 0), TimeGridView.DroppedStart(start, grab, drop, 30));
        }

        [Fact]
        public void TimeGrid_SingleDayEventDroppedPastMidnightStaysClamped()
        {
            var start = new DateTime(2026, 3, 2, 9, 0, 0);
            var grab = new DateTime(2026, 3, 2, 9, 0, 0);
            var drop = new DateTime(2026, 3, 2, 23, 59, 0);
            Assert.Equal(new DateTime(2026, 3, 2, 23, 30, 0), TimeGridView.DroppedStart(start, grab, drop, 30));
        }

        [Fact]
        public void Month_GrabbingFourthDayOfBarAndMovingOneCellMovesOneDay()
        {
            var start = new DateTime(2026, 3, 2);
            Assert.Equal(new DateTime(2026, 3, 3),
                MonthView.DroppedStart(start, new DateTime(2026, 3, 5), new DateTime(2026, 3, 6), true));
        }

        [Fact]
        public void Month_MovingBarBackwardsKeepsTimeOfDay()
        {
            var start = new DateTime(2026, 3, 10, 14, 30, 0);
            Assert.Equal(new DateTime(2026, 3, 8, 14, 30, 0),
                MonthView.DroppedStart(start, new DateTime(2026, 3, 12), new DateTime(2026, 3, 10), false));
        }
    }
}
