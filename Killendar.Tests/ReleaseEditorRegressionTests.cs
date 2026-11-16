using System;
using System.Linq;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class ReleaseEditorRegressionTests
    {
        [Fact]
        public void ZonedTitleEditKeepsLaterOccurrenceOfRepeatedHour()
        {
            TimeZones.Load("1", "");
            try
            {
                using var fixture = new AppointmentFixture();
                var original = new Killendar.Models.CalendarEvent
                {
                    Title = "Later hour", Start = new DateTime(2026, 10, 25, 1, 30, 45, DateTimeKind.Utc).ToLocalTime(),
                    End = new DateTime(2026, 10, 25, 2, 30, 45, DateTimeKind.Utc).ToLocalTime(),
                };
                TimeZones.Apply(original, "Europe/London");
                fixture.Store.Add(original);
                fixture.Editor.Load(Assert.Single(fixture.Store.Events));
                fixture.View.FieldTitle = "Renamed";
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);
                Assert.Equal(new DateTime(2026, 10, 25, 1, 30, 45, DateTimeKind.Utc), Assert.Single(fixture.Store.Events).Start.ToUniversalTime());
            }
            finally { TimeZones.Load("0", ""); }
        }

        [Fact]
        public void TemplateDeleteFailureReturnsErrorAndKeepsTemplate()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 11, 15, 9, 0, 0));
            fixture.View.FieldTitle = "Keep template";
            Assert.True(fixture.Editor.SaveTemplate());
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.OpenReadOnly(path);

            Assert.NotNull(fixture.Store.DeleteTemplate("Keep template"));
            Assert.Single(fixture.Store.ListTemplates());
        }

        [Fact]
        public void TemplateWriteFailureKeepsDraftAndReportsError()
        {
            using var fixture = new AppointmentFixture();
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.OpenReadOnly(path);
            fixture.Editor.NewAt(new DateTime(2026, 11, 15, 9, 0, 0));
            fixture.View.FieldTitle = "Unsaved template";

            Assert.False(fixture.Editor.SaveTemplate());

            Assert.NotNull(fixture.View.Error);
            Assert.Equal("Unsaved template", fixture.View.FieldTitle);
            Assert.Empty(fixture.Store.ListTemplates());
        }

        [Fact]
        public void ZonedStartMoveKeepsCalendarDayDurationAcrossLaterClockChange()
        {
            var style = DateFormatManager.Current;
            TimeZones.Load("1", "");
            DateFormatManager.Apply(DateStyle.Iso);
            try
            {
                using var fixture = new AppointmentFixture();
                fixture.Store.ImportEvents(IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:nominal\r\nDTSTART;TZID=Europe/London:20261024T090000\r\nDURATION:P1DT2H\r\nRRULE:FREQ=DAILY;COUNT=160\r\nSUMMARY:Nominal\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n").Events);
                fixture.Editor.Load(Assert.Single(fixture.Store.Events));
                fixture.View.EditWholeSeries = true;
                fixture.View.FieldStartDate = "2026-10-25";

                fixture.Editor.Save();

                Assert.Null(fixture.View.Error);
                var saved = Assert.Single(fixture.Store.Events);
                Assert.Equal(1, saved.DurationDays);
                Assert.Equal(new DateTime(2026, 10, 26, 11, 0, 0, DateTimeKind.Utc), saved.End.ToUniversalTime());
                var later = Assert.Single(fixture.Store.GetOnDay(new DateTime(2027, 3, 27)), e => e.Start.ToUniversalTime().Date == new DateTime(2027, 3, 27));
                Assert.Equal(new DateTime(2027, 3, 28, 10, 0, 0, DateTimeKind.Utc), later.End.ToUniversalTime());
            }
            finally { TimeZones.Load("0", ""); DateFormatManager.Apply(style); }
        }

        [Fact]
        public void ZonedTitleEditPreservesOriginalInstants()
        {
            TimeZones.Load("1", "");
            try
            {
                using var fixture = new AppointmentFixture();
                var parsed = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:precision\r\nDTSTART;TZID=Asia/Tokyo:20261116T090045\r\nDTEND;TZID=Asia/Tokyo:20261116T100045\r\nSUMMARY:Original\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
                fixture.Store.ImportEvents(parsed.Events);
                var original = Assert.Single(fixture.Store.Events).Clone();
                fixture.Editor.Load(original);
                fixture.View.FieldTitle = "Title only";

                fixture.Editor.Save();

                Assert.Null(fixture.View.Error);
                var saved = Assert.Single(fixture.Store.Events);
                Assert.Equal(new DateTime(2026, 11, 16, 0, 0, 45, DateTimeKind.Utc), saved.Start.ToUniversalTime());
                Assert.Equal(original.End.ToUniversalTime(), saved.End.ToUniversalTime());
            }
            finally { TimeZones.Load("0", ""); }
        }
    }
}
