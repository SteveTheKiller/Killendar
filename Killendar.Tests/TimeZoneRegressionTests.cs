using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S14: an appointment can be given a time zone; its times are typed and kept on that clock.
    [Collection("Calendar settings")]
    public sealed class TimeZoneRegressionTests
    {
        private const string Tokyo = "Tokyo Standard Time";   // no daylight saving
        private static readonly DateTime At = new(2026, 10, 13, 9, 0, 0);

        private static void WithZones(Action body)
        {
            TimeZones.Load("1", "");
            try { body(); }
            finally { TimeZones.Load("0", ""); }
        }

        [Fact]
        public void TimesAreTypedOnTheChosenZonesClock()
        {
            WithZones(() =>
            {
                using var fixture = new AppointmentFixture();
                fixture.Editor.NewAt(At);
                fixture.View.FieldTitle = "Call Tokyo";
                fixture.View.FieldZoneId = Tokyo;

                fixture.Editor.Save();

                var saved = Assert.Single(fixture.Store.Events);
                Assert.Equal(new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc), saved.Start.ToUniversalTime());
                Assert.Equal(Tokyo, TimeZones.ZoneOf(saved));
            });
        }

        [Fact]
        public void ReopeningShowsTheZonesClock()
        {
            WithZones(() =>
            {
                using var fixture = new AppointmentFixture();
                fixture.Editor.NewAt(At);
                fixture.View.FieldTitle = "Call Tokyo";
                fixture.View.FieldZoneId = Tokyo;
                fixture.Editor.Save();

                fixture.Editor.Load(Assert.Single(fixture.Store.Events));

                Assert.Equal(Tokyo, fixture.View.FieldZoneId);
                Assert.Equal(DateFormatManager.Time(At), fixture.View.FieldStartTime);
            });
        }

        [Fact]
        public void ARepeatKeepsTheZonesClockAcrossThisComputersClockChange()
        {
            WithZones(() =>
            {
                using var fixture = new AppointmentFixture();
                fixture.Editor.NewAt(At);
                fixture.View.FieldTitle = "Weekly Tokyo";
                fixture.View.FieldZoneId = Tokyo;
                fixture.View.FieldRepeat = RepeatFreq.Weekly;
                fixture.View.FieldRepeatEveryN = 1;
                fixture.View.FieldRepeatDays = [At.DayOfWeek];

                fixture.Editor.Save();

                var tokyo = TimeZoneInfo.FindSystemTimeZoneById(Tokyo);
                var starts = fixture.Store.GetInRange(At.Date, At.Date.AddDays(60)).Select(o => o.Start).ToList();
                Assert.True(starts.Count >= 8);
                Assert.All(starts, s => Assert.Equal(9, TimeZones.ToZone(s, tokyo).Hour));
            });
        }

        [Fact]
        public void ChoosingThisComputerAgainDropsTheZone()
        {
            WithZones(() =>
            {
                using var fixture = new AppointmentFixture();
                fixture.Editor.NewAt(At);
                fixture.View.FieldTitle = "Call";
                fixture.View.FieldZoneId = Tokyo;
                fixture.Editor.Save();
                fixture.Editor.Load(Assert.Single(fixture.Store.Events));

                fixture.View.FieldZoneId = "";
                fixture.Editor.Save();

                var saved = Assert.Single(fixture.Store.Events);
                Assert.Equal("", TimeZones.ZoneOf(saved));
                Assert.Equal(RecurrenceTimeBasis.Local, saved.RepeatTimeBasis);
            });
        }

        [Fact]
        public void OffByDefaultTheZoneIsIgnored()
        {
            TimeZones.Load(null, null);
            Assert.False(TimeZones.Show);
            Assert.Null(TimeZones.Second);
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(At);
            fixture.View.FieldTitle = "Local";
            fixture.View.FieldZoneId = Tokyo;

            fixture.Editor.Save();

            var saved = Assert.Single(fixture.Store.Events);
            Assert.Equal(At, saved.Start);
            Assert.Equal("", TimeZones.ZoneOf(saved));
        }

        [Fact]
        public void TheZoneIsKeptInTheFileAndAcrossReopening()
        {
            WithZones(() =>
            {
                using var fixture = new AppointmentFixture();
                fixture.Editor.NewAt(At);
                fixture.View.FieldTitle = "Call Tokyo";
                fixture.View.FieldZoneId = Tokyo;
                fixture.Editor.Save();
                string path = fixture.Store.FilePath;
                fixture.Store.Close();
                fixture.Store.Open(path);

                Assert.Equal(Tokyo, TimeZones.ZoneOf(Assert.Single(fixture.Store.Events)));
            });
        }

        [Fact]
        public void SecondZoneNeedsTheSettingOn()
        {
            TimeZones.Load("0", Tokyo);
            Assert.Null(TimeZones.Second);
            TimeZones.Load("1", Tokyo);
            Assert.Equal(Tokyo, TimeZones.Second?.Id);
            TimeZones.Load("0", "");
        }

        [Theory]
        [InlineData(0, "UTC")]
        [InlineData(330, "UTC+05:30")]
        [InlineData(-300, "UTC-05:00")]
        public void OffsetLabel(int minutes, string expected)
        {
            var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromMinutes(minutes), "test", "test");
            Assert.Equal(expected, TimeZones.Offset(zone, At));
        }
    }
}
