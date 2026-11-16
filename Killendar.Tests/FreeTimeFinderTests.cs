using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Find open time: gaps of at least the asked length across the shown calendars, inside work
    // hours when asked. All-day appointments do not count as busy.
    [Collection("Calendar settings")]
    public sealed class FreeTimeFinderTests : IDisposable
    {
        // Wednesday 18 November 2026, 10:15.
        private static readonly DateTime Now = new DateTime(2026, 11, 18, 10, 15, 0);

        public FreeTimeFinderTests() => WorkSchedule.Load("1,2,3,4,5", "9-17");
        public void Dispose() => WorkSchedule.Load(null, null);

        private static CalendarEvent Busy(int day, int fromHour, int fromMinute, int toHour, int toMinute) => new CalendarEvent
        {
            Title = "busy",
            Start = new DateTime(2026, 11, day, fromHour, fromMinute, 0),
            End = new DateTime(2026, 11, day, toHour, toMinute, 0),
        };

        [Fact]
        public void TheFirstGapStartsAtTheNextQuarterHour()
        {
            var gaps = FreeTime.Find(new CalendarEvent[0], Now, Now.Date.AddDays(1), TimeSpan.FromHours(1), workHoursOnly: true);
            var first = gaps.First();
            Assert.Equal(new DateTime(2026, 11, 18, 10, 15, 0), first.Start);
            Assert.Equal(new DateTime(2026, 11, 18, 17, 0, 0), first.End);
        }

        [Fact]
        public void AppointmentsSplitTheDayAndShortGapsAreDropped()
        {
            var busy = new[] { Busy(18, 11, 0, 12, 30), Busy(18, 13, 0, 15, 0) };
            var gaps = FreeTime.Find(busy, Now, Now.Date.AddDays(1), TimeSpan.FromHours(1), workHoursOnly: true);
            Assert.Equal(new[]
            {
                (new DateTime(2026, 11, 18, 15, 0, 0), new DateTime(2026, 11, 18, 17, 0, 0)),
            }, gaps.Select(g => (g.Start, g.End)));
        }

        [Fact]
        public void OverlappingAppointmentsMergeIntoOneBusyBlock()
        {
            var busy = new[] { Busy(18, 11, 0, 13, 0), Busy(18, 12, 0, 16, 30) };
            var gaps = FreeTime.Find(busy, Now, Now.Date.AddDays(1), TimeSpan.FromMinutes(30), workHoursOnly: true);
            Assert.Equal(new[]
            {
                (new DateTime(2026, 11, 18, 10, 15, 0), new DateTime(2026, 11, 18, 11, 0, 0)),
                (new DateTime(2026, 11, 18, 16, 30, 0), new DateTime(2026, 11, 18, 17, 0, 0)),
            }, gaps.Select(g => (g.Start, g.End)));
        }

        [Fact]
        public void AllDayAppointmentsAreNotBusy()
        {
            var allDay = new CalendarEvent { Title = "trip", AllDay = true, Start = new DateTime(2026, 11, 18), End = new DateTime(2026, 11, 19) };
            var gaps = FreeTime.Find(new[] { allDay }, Now, Now.Date.AddDays(1), TimeSpan.FromHours(1), workHoursOnly: true);
            Assert.Single(gaps);
        }

        [Fact]
        public void WorkHoursSkipTheWeekend()
        {
            // Friday 20 to Monday 23: Saturday and Sunday are not workdays.
            var gaps = FreeTime.Find(new CalendarEvent[0], new DateTime(2026, 11, 20, 8, 0, 0), new DateTime(2026, 11, 24), TimeSpan.FromHours(1), workHoursOnly: true);
            Assert.Equal(new[] { 20, 23 }, gaps.Select(g => g.Start.Day));
            Assert.All(gaps, g => Assert.Equal(9, g.Start.Hour));
        }

        [Fact]
        public void WithoutWorkHoursAGapRunsAcrossMidnight()
        {
            var busy = new[] { Busy(18, 11, 0, 23, 0) };
            var gaps = FreeTime.Find(busy, Now, new DateTime(2026, 11, 19, 8, 0, 0), TimeSpan.FromHours(2), workHoursOnly: false);
            var last = gaps.Last();
            Assert.Equal(new DateTime(2026, 11, 18, 23, 0, 0), last.Start);
            Assert.Equal(new DateTime(2026, 11, 19, 8, 0, 0), last.End);
        }

        [Fact]
        public void ABusyBlockThatEndsMidQuarterLeavesTheGapExact()
        {
            var busy = new[] { Busy(18, 10, 0, 14, 10) };
            var gaps = FreeTime.Find(busy, Now, Now.Date.AddDays(1), TimeSpan.FromHours(1), workHoursOnly: true);
            Assert.Equal(new DateTime(2026, 11, 18, 14, 10, 0), gaps.Single().Start);
        }
    }
}
