using System;
using System.Globalization;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // UX-003: every displayed time follows one policy: the chosen date style and Follow Windows,
    // 12-hour or 24-hour. Interchange formats are untouched.
    [Collection("Calendar settings")]
    public sealed class TimeDisplayPolicyRegressionTests : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly DateStyle _date = DateFormatManager.Current;
        private readonly TimeStyle _time = DateFormatManager.CurrentTime;

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            DateFormatManager.Apply(_date);
            DateFormatManager.ApplyTime(_time);
        }

        private static readonly DateTime Afternoon = new(2026, 10, 22, 13, 5, 0);

        [Theory]
        [InlineData("en-US", TimeStyle.FollowWindows, "1:05 PM")]
        [InlineData("de-DE", TimeStyle.FollowWindows, "13:05")]
        [InlineData("ja-JP", TimeStyle.FollowWindows, "13:05")]
        [InlineData("en-US", TimeStyle.TwentyFourHour, "13:05")]
        [InlineData("de-DE", TimeStyle.TwelveHour, "1:05 PM")]
        public void TimeFollowsTheChosenClock(string culture, TimeStyle style, string expected)
        {
            Use(culture, DateStyle.Iso, style);
            Assert.Equal(expected, DateFormatManager.Time(Afternoon));
        }

        [Theory]
        [InlineData(TimeStyle.TwelveHour, 0, "12:00 AM")]
        [InlineData(TimeStyle.TwelveHour, 12, "12:00 PM")]
        [InlineData(TimeStyle.TwentyFourHour, 0, "00:00")]
        [InlineData(TimeStyle.TwentyFourHour, 12, "12:00")]
        public void MidnightAndNoonAreUnambiguous(TimeStyle style, int hour, string expected)
        {
            Use("en-US", DateStyle.Iso, style);
            Assert.Equal(expected, DateFormatManager.Time(new DateTime(2026, 10, 22, hour, 0, 0)));
        }

        [Theory]
        [InlineData("en-US", DateStyle.US, TimeStyle.TwelveHour, "10/22 11:30 PM - 10/23 1:30 AM")]
        [InlineData("en-GB", DateStyle.EU, TimeStyle.TwentyFourHour, "22/10 23:30 - 23/10 01:30")]
        [InlineData("en-US", DateStyle.Iso, TimeStyle.TwentyFourHour, "10-22 23:30 - 10-23 01:30")]
        public void MultiDayLabelUsesTheDateStyleAndClock(string culture, DateStyle date, TimeStyle time, string expected)
        {
            Use(culture, date, time);
            var ev = new CalendarEvent { Start = new DateTime(2026, 10, 22, 23, 30, 0), End = new DateTime(2026, 10, 23, 1, 30, 0) };
            Assert.Equal(expected, ev.TimeLabel);
        }

        [Fact]
        public void SameDayLabelUsesTheClock()
        {
            Use("en-US", DateStyle.Iso, TimeStyle.TwentyFourHour);
            var ev = new CalendarEvent { Start = Afternoon, End = Afternoon.AddHours(1) };
            Assert.Equal("13:05 - 14:05", ev.TimeLabel);
        }

        [Theory]
        [InlineData(TimeStyle.TwelveHour, "1:05", "1 PM")]
        [InlineData(TimeStyle.TwentyFourHour, "13:05", "13:00")]
        public void CompactChipAndGutterLabelsFollowTheClock(TimeStyle style, string chip, string hour)
        {
            Use("en-US", DateStyle.Iso, style);
            Assert.Equal(chip, DateFormatManager.CompactTime(Afternoon));
            Assert.Equal(hour, DateFormatManager.HourLabel(13));
        }

        [Fact]
        public void ClockChoicePersistsAndNotifies()
        {
            string? saved = null;
            var previous = DateFormatManager.SetSetting;
            int changes = 0;
            void Count() => changes++;
            DateFormatManager.Changed += Count;
            try
            {
                DateFormatManager.SetSetting = (key, value) => { if (key == "TimeStyle") saved = value; };
                DateFormatManager.ApplyTime(TimeStyle.TwentyFourHour);
            }
            finally { DateFormatManager.SetSetting = previous; DateFormatManager.Changed -= Count; }

            Assert.Equal("TwentyFourHour", saved);
            Assert.Equal(1, changes);
        }

        [Theory]
        [InlineData("de-DE", TimeStyle.TwelveHour, "1:00 PM")]
        [InlineData("ja-JP", TimeStyle.FollowWindows, "13:00")]
        [InlineData("en-US", TimeStyle.TwentyFourHour, "13:00")]
        public void EditorShowsAndSavesTheChosenClock(string culture, TimeStyle style, string shown)
        {
            Use(culture, DateStyle.Iso, style);
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 13, 0, 0));
            fixture.View.FieldTitle = "Clock";

            Assert.Equal(shown, fixture.View.FieldStartTime);
            fixture.Editor.Save();

            Assert.Null(fixture.View.Error);
            Assert.Equal(new DateTime(2026, 10, 22, 13, 0, 0), Assert.Single(fixture.Store.Events).Start);
        }

        [Fact]
        public void ClockChangeReformatsTheOpenEditorWithoutMakingItDirty()
        {
            Use("en-US", DateStyle.Iso, TimeStyle.TwelveHour);
            using var fixture = new AppointmentFixture();
            var ev = new CalendarEvent { Title = "Loaded", Start = Afternoon, End = Afternoon.AddHours(1) };
            fixture.Store.Add(ev);
            fixture.Editor.Load(ev);

            fixture.Editor.ChangeTimeStyle(TimeStyle.TwentyFourHour);

            Assert.Equal("13:05", fixture.View.FieldStartTime);
            Assert.Equal("14:05", fixture.View.FieldEndTime);
            Assert.False(fixture.Editor.HasUnsavedChanges);
        }

        [Fact]
        public void DateStyleChangeDoesNotMakeALoadedAppointmentDirty()
        {
            Use("en-US", DateStyle.Iso, TimeStyle.TwelveHour);
            using var fixture = new AppointmentFixture();
            var ev = new CalendarEvent { Title = "Loaded", Start = Afternoon, End = Afternoon.AddHours(1) };
            fixture.Store.Add(ev);
            fixture.Editor.Load(ev);

            fixture.Editor.ChangeDateStyle(DateStyle.EU);

            Assert.Equal("22/10/2026", fixture.View.FieldStartDate);
            Assert.False(fixture.Editor.HasUnsavedChanges);
        }

        [Fact]
        public void StyleChangeKeepsARealEditDirty()
        {
            Use("en-US", DateStyle.Iso, TimeStyle.TwelveHour);
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(Afternoon);
            fixture.View.FieldTitle = "Typed";

            fixture.Editor.ChangeTimeStyle(TimeStyle.TwentyFourHour);

            Assert.True(fixture.Editor.HasUnsavedChanges);
        }

        private static void Use(string culture, DateStyle date, TimeStyle time)
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture, useUserOverride: false);
            DateFormatManager.Apply(date);
            DateFormatManager.ApplyTime(time);
        }
    }
}
