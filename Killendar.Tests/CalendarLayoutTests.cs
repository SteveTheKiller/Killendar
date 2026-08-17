using System;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class CalendarLayoutTests
    {
        [Fact]
        public void TimedEventIsClippedToVisibleDay()
        {
            var day = new DateTime(2026, 8, 16);
            var ev = Event(day.AddHours(-2), day.AddHours(3));

            var placement = Assert.Single(CalendarLayout.PlaceTimedDay(new[] { ev }, day));

            Assert.Equal(day, placement.VisibleStart);
            Assert.Equal(day.AddHours(3), placement.VisibleEnd);
        }

        [Fact]
        public void SeparateOverlapClustersRecoverFullWidth()
        {
            var day = new DateTime(2026, 8, 16);
            var first = Event(day.AddHours(9), day.AddHours(11));
            var overlap = Event(day.AddHours(10), day.AddHours(12));
            var later = Event(day.AddHours(15), day.AddHours(16));

            var placements = CalendarLayout.PlaceTimedDay(new[] { first, overlap, later }, day);

            Assert.Equal(2, placements[0].LaneCount);
            Assert.Equal(2, placements[1].LaneCount);
            Assert.Equal(1, placements[2].LaneCount);
        }

        [Fact]
        public void EventEndingAtMidnightDoesNotAppearOnFollowingDay()
        {
            var day = new DateTime(2026, 8, 16);
            var ev = Event(day.AddHours(-3), day);

            Assert.Empty(CalendarLayout.PlaceTimedDay(new[] { ev }, day));
        }

        [Fact]
        public void AllDayEventOccupiesOnlyItsCoveredWeekColumns()
        {
            var monday = new DateTime(2026, 8, 17);
            var ev = Event(monday.AddDays(1), monday.AddDays(3));
            ev.AllDay = true;

            var placement = Assert.Single(
                CalendarLayout.PlaceAllDayRange(new[] { ev }, monday, 7));

            Assert.Equal(1, placement.StartColumn);
            Assert.Equal(2, placement.ColumnSpan);
        }

        [Fact]
        public void OverlappingAllDayRunsUseSeparateRows()
        {
            var monday = new DateTime(2026, 8, 17);
            var first = Event(monday, monday.AddDays(3));
            var second = Event(monday.AddDays(2), monday.AddDays(4));
            first.AllDay = second.AllDay = true;

            var placements = CalendarLayout.PlaceAllDayRange(new[] { first, second }, monday, 7);

            Assert.Equal(0, placements[0].Row);
            Assert.Equal(1, placements[1].Row);
        }

        [Fact]
        public void MultiDayMonthRunIsOneContinuousPlacement()
        {
            var monday = new DateTime(2026, 8, 17);
            var ev = Event(monday.AddHours(9), monday.AddDays(3).AddHours(10));

            var placement = Assert.Single(
                CalendarLayout.PlaceMultiDayRange(new[] { ev }, monday, 7));

            Assert.Equal(0, placement.StartColumn);
            Assert.Equal(4, placement.ColumnSpan);
            Assert.Same(ev, placement.Event);
        }

        [Fact]
        public void OneDayAllDayEventIsNotTreatedAsMultiDay()
        {
            var day = new DateTime(2026, 8, 17);
            var ev = Event(day, day.AddDays(1));
            ev.AllDay = true;

            Assert.False(CalendarLayout.IsMultiDay(ev));
        }

        private static CalendarEvent Event(DateTime start, DateTime end) => new()
        {
            Title = "test",
            Start = start,
            End = end,
        };
    }
}
