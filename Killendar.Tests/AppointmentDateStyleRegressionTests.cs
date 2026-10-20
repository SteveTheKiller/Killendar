using System;
using System.Globalization;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class AppointmentDateStyleRegressionTests
    {
        [Fact]
        public void DateStyleChangeLeavesIncompleteInputForTheUserToComplete()
        {
            var previous = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                DateFormatManager.Apply(DateStyle.US);
                fixture.Editor.NewAt(new DateTime(2026, 11, 12, 9, 0, 0));
                fixture.View.FieldTitle = "Incomplete";
                fixture.View.FieldStartDate = "11/";
                fixture.View.RepeatDateText = "12/";
                fixture.Editor.ChangeDateStyle(DateStyle.EU);
                Assert.Equal("11/", fixture.View.FieldStartDate);
                Assert.Equal("12/", fixture.View.RepeatDateText);
                fixture.Editor.Save();
                Assert.NotNull(fixture.View.Error);
                Assert.False(fixture.View.Closed);
                Assert.Empty(fixture.Store.Events);
            }
            finally { DateFormatManager.Apply(previous); }
        }

        [Theory]
        [InlineData(DateStyle.US, DateStyle.EU)]
        [InlineData(DateStyle.EU, DateStyle.US)]
        [InlineData(DateStyle.US, DateStyle.Iso)]
        [InlineData(DateStyle.Iso, DateStyle.US)]
        [InlineData(DateStyle.EU, DateStyle.Iso)]
        [InlineData(DateStyle.Iso, DateStyle.EU)]
        public void ChangingDateStylePreservesDatesDurationAndRecurrenceEnd(DateStyle before, DateStyle after)
        {
            var previous = DateFormatManager.Current;
            var culture = CultureInfo.CurrentCulture;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                DateFormatManager.Apply(before);
                var original = new CalendarEvent
                {
                    Title = "Keep dates", Start = new DateTime(2026, 11, 12, 15, 30, 0),
                    End = new DateTime(2026, 11, 14, 16, 30, 0), Repeat = RepeatFreq.Daily,
                    RepeatUntil = new DateTime(2026, 12, 11)
                };
                fixture.Store.Add(original);
                fixture.Editor.Load(Assert.Single(fixture.Store.GetOnDay(new DateTime(2026, 11, 12))));
                fixture.View.EditWholeSeries = true;
                fixture.Editor.ChangeDateStyle(after);
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);
                var saved = fixture.Store.GetById(original.Id)!;
                Assert.Single(fixture.Store.Events);
                Assert.Equal(new DateTime(2026, 11, 12, 15, 30, 0), saved.Start);
                Assert.Equal(new DateTime(2026, 11, 14, 16, 30, 0), saved.End);
                Assert.Equal(new DateTime(2026, 12, 11), saved.RepeatUntil);
            }
            finally { CultureInfo.CurrentCulture = culture; DateFormatManager.Apply(previous); }
        }
    }
}
