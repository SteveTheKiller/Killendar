using System;
using System.Globalization;
using Killendar.Services;
using Killendar.Features;
using Killendar.Models;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class AppointmentDateBoundsRegressionTests
    {
        [Theory]
        [InlineData(9998, 6, 1, 9)]
        [InlineData(9999, 12, 31, 23)]
        public void RepeatPreviewStaysWithinSupportedDates(int year, int month, int day, int hour)
        {
            var probe = new CalendarEvent { Start = new DateTime(year, month, day, hour, 30, 0),
                Repeat = RepeatFreq.Daily, RepeatInterval = 1 };
            CalendarEvent[]? result = null;
            Assert.Null(Record.Exception(() => result = AppointmentEditor.RepeatPreview(probe)));
            Assert.NotNull(result);
            Assert.InRange(result!.Length, 1, 4);
            Assert.Equal(probe.Start, result[0].Start);
            Assert.Equal(default, probe.End);
        }

        [Fact]
        public void TypingAnUnrepresentableEndDoesNotThrowOrLoseTheOriginalDuration()
        {
            var previous = DateFormatManager.Current;
            var culture = CultureInfo.CurrentCulture;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                DateFormatManager.Apply(DateStyle.Iso);
                fixture.Editor.NewAt(new DateTime(2026, 11, 12, 9, 0, 0));
                fixture.View.FieldTitle = "Boundary";
                fixture.View.FieldStartDate = "9999-12-31";
                Assert.Null(Record.Exception(() => fixture.View.FieldStartTime = "23:30"));
                fixture.Editor.Save();
                Assert.NotNull(fixture.View.Error);
                Assert.Empty(fixture.Store.Events);
                fixture.View.FieldStartTime = "11:00";
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);
                Assert.Equal(new DateTime(9999, 12, 31, 11, 0, 0), Assert.Single(fixture.Store.Events).Start);
                Assert.Equal(new DateTime(9999, 12, 31, 12, 0, 0), Assert.Single(fixture.Store.Events).End);
            }
            finally { CultureInfo.CurrentCulture = culture; DateFormatManager.Apply(previous); }
        }

        [Fact]
        public void NewAppointmentThatCannotFitIsRejectedWithoutOpeningOrSaving()
        {
            using var fixture = new AppointmentFixture();
            Assert.Null(Record.Exception(() => fixture.Editor.NewAt(new DateTime(9999, 12, 31, 23, 30, 0))));
            Assert.NotNull(fixture.View.Error);
            Assert.Empty(fixture.Store.Events);
        }

        [Fact]
        public void AllDayAppointmentAtLastDateIsRejectedWithoutThrowing()
        {
            var previous = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                DateFormatManager.Apply(DateStyle.Iso);
                fixture.Editor.NewAt(new DateTime(9999, 12, 31, 9, 0, 0));
                fixture.View.FieldTitle = "Last all-day appointment";
                fixture.Editor.ToggleAllDay();
                Assert.Null(Record.Exception(() => fixture.Editor.Save()));
                Assert.Equal("Str_Err_DateRange", fixture.View.Error);
                Assert.False(fixture.View.Closed);
                Assert.Empty(fixture.Store.Events);
            }
            finally { DateFormatManager.Apply(previous); }
        }
    }
}
