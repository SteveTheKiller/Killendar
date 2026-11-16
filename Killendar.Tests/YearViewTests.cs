using System;
using Killendar.Models;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    // Year view: twelve months, busier days shaded darker, prev and next move a year.
    public sealed class YearViewTests
    {
        private static CalendarEvent Timed(int month, int day, int hour, double hours) => new CalendarEvent
        {
            Title = "x",
            Start = new DateTime(2026, month, day, hour, 0, 0),
            End = new DateTime(2026, month, day, hour, 0, 0).AddHours(hours),
        };

        [Fact]
        public void EachAppointmentOnADayRaisesItsLevel()
        {
            var levels = YearView.BusyLevels(new[] { Timed(3, 4, 9, 1), Timed(3, 4, 13, 1), Timed(3, 5, 9, 1) }, 2026);
            Assert.Equal(2, levels[new DateTime(2026, 3, 4)]);
            Assert.Equal(1, levels[new DateTime(2026, 3, 5)]);
            Assert.False(levels.ContainsKey(new DateTime(2026, 3, 6)));
        }

        [Fact]
        public void LevelsStopAtTheDarkestShade()
        {
            var many = new CalendarEvent[9];
            for (int i = 0; i < many.Length; i++) many[i] = Timed(6, 1, 8 + i, 0.5);
            Assert.Equal(YearView.MaxLevel, YearView.BusyLevels(many, 2026)[new DateTime(2026, 6, 1)]);
        }

        [Fact]
        public void AMultiDayAppointmentShadesEveryDayItTouches()
        {
            var trip = new CalendarEvent { Title = "trip", AllDay = true, Start = new DateTime(2026, 7, 10), End = new DateTime(2026, 7, 13) };
            var levels = YearView.BusyLevels(new[] { trip }, 2026);
            Assert.Equal(1, levels[new DateTime(2026, 7, 10)]);
            Assert.Equal(1, levels[new DateTime(2026, 7, 12)]);
            Assert.False(levels.ContainsKey(new DateTime(2026, 7, 13)));
        }

        [Fact]
        public void AnEveningThatEndsAtMidnightStaysOnItsOwnDay()
        {
            var levels = YearView.BusyLevels(new[] { Timed(9, 9, 22, 2) }, 2026);
            Assert.True(levels.ContainsKey(new DateTime(2026, 9, 9)));
            Assert.False(levels.ContainsKey(new DateTime(2026, 9, 10)));
        }

        [Fact]
        public void DaysOutsideTheYearAreLeftOut()
        {
            var nye = new CalendarEvent { Title = "nye", Start = new DateTime(2026, 12, 31, 22, 0, 0), End = new DateTime(2027, 1, 1, 2, 0, 0) };
            var levels = YearView.BusyLevels(new[] { nye }, 2026);
            Assert.True(levels.ContainsKey(new DateTime(2026, 12, 31)));
            Assert.False(levels.ContainsKey(new DateTime(2027, 1, 1)));
        }

        [Theory]
        [InlineData(2026, 3, 15, 1, 2027, 3, 15)]
        [InlineData(2026, 3, 15, -1, 2025, 3, 15)]
        [InlineData(2028, 2, 29, 1, 2029, 2, 28)]
        public void StepMovesOneYear(int y, int m, int d, int direction, int ey, int em, int ed)
            => Assert.Equal(new DateTime(ey, em, ed), YearView.StepBy(new DateTime(y, m, d), direction));
    }
}
