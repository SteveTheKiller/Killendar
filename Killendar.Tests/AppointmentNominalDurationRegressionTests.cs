using System;
using System.Globalization;
using System.Linq;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class AppointmentNominalDurationRegressionTests
    {
        [Fact]
        public void ExplicitEndEditReplacesNominalDurationWithEnteredEndpoint()
        {
            Assert.Equal("Pacific Standard Time", TimeZoneInfo.Local.Id);
            var culture = CultureInfo.CurrentCulture;
            var style = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                DateFormatManager.Apply(DateStyle.Iso);
                const string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:editor-nominal-duration\r\n" +
                    "DTSTART;TZID=America/New_York:20261101T003000\r\nDURATION:P1DT2H\r\n" +
                    "RRULE:FREQ=DAILY;COUNT=2\r\nSUMMARY:Nominal duration\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
                Assert.Equal(1, fixture.Store.ImportEvents(IcsService.ParseText(text).Events));
                var master = Assert.Single(fixture.Store.Events);
                Assert.Equal(1, master.DurationDays);
                Assert.Equal(TimeSpan.FromHours(2).Ticks, master.DurationTimeTicks);
                fixture.Editor.Load(master);
                fixture.View.EditWholeSeries = true;
                fixture.View.FieldEndTime = "11:45 PM";

                fixture.Editor.Save();

                Assert.Null(fixture.View.Error);
                Assert.Null(fixture.Store.LoadError);
                var saved = Assert.Single(fixture.Store.Events);
                Assert.Equal(0, saved.DurationDays);
                Assert.Equal(0, saved.DurationTimeTicks);
                Assert.Equal(new DateTime(2026, 11, 2, 7, 45, 0, DateTimeKind.Utc), saved.End.ToUniversalTime());
                string path = fixture.Store.FilePath;
                fixture.Store.Close();
                fixture.Store.Open(path);
                Assert.Null(fixture.Store.LoadError);
                saved = Assert.Single(fixture.Store.Events);
                Assert.Equal(0, saved.DurationDays);
                Assert.Equal(0, saved.DurationTimeTicks);
                Assert.Equal(new DateTime(2026, 11, 2, 7, 45, 0, DateTimeKind.Utc), saved.End.ToUniversalTime());
            }
            finally { CultureInfo.CurrentCulture = culture; DateFormatManager.Apply(style); }
        }

        [Fact]
        public void MetadataOnlyEditPreservesNominalDaysAndEveryUtcEndpoint()
        {
            WithImported("DTSTART;TZID=America/New_York:20261101T003000\r\n", fixture =>
            {
                fixture.View.FieldTitle = "Metadata changed";
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);
                Reopen(fixture);
                var master = Assert.Single(fixture.Store.Events);
                Assert.Equal(1, master.DurationDays);
                Assert.Equal(TimeSpan.FromHours(2).Ticks, master.DurationTimeTicks);
                var rows = fixture.Store.GetInRange(new DateTime(2026, 10, 31), new DateTime(2026, 11, 5));
                Assert.Equal(new[] { new DateTime(2026, 11, 1, 4, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 2, 5, 30, 0, DateTimeKind.Utc) }, rows.Select(row => row.Start.ToUniversalTime()).ToArray());
                Assert.Equal(new[] { new DateTime(2026, 11, 2, 7, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 3, 7, 30, 0, DateTimeKind.Utc) }, rows.Select(row => row.End.ToUniversalTime()).ToArray());
            });
        }

        [Fact]
        public void StartMoveRecomputesNominalAutoEndAcrossDifferentDisplayAndSourceDstDates()
        {
            WithImported("DTSTART;TZID=Europe/London:20261024T090000\r\n", fixture =>
            {
                fixture.View.FieldStartDate = "2026-10-25";
                Assert.Equal("2026-10-26", fixture.View.FieldEndDate);
                Assert.Equal("3:00 AM", fixture.View.FieldEndTime);
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);
                Reopen(fixture);
                var master = Assert.Single(fixture.Store.Events);
                Assert.Equal(1, master.DurationDays);
                Assert.Equal(TimeSpan.FromHours(2).Ticks, master.DurationTimeTicks);
                Assert.Equal(new DateTime(2026, 10, 25, 8, 0, 0), master.RepeatAnchor);
                Assert.Equal(new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc), master.Start.ToUniversalTime());
                Assert.Equal(new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc), master.End.ToUniversalTime());
            });
        }

        [Fact]
        public void AllDayChangeClearsNominalTimedDuration()
        {
            WithImported("DTSTART;TZID=America/New_York:20261101T003000\r\n", fixture =>
            {
                fixture.Editor.ToggleAllDay();
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);
                Reopen(fixture);
                var master = Assert.Single(fixture.Store.Events);
                Assert.True(master.AllDay);
                Assert.Equal(0, master.DurationDays);
                Assert.Equal(0, master.DurationTimeTicks);
            });
        }

        private static void WithImported(string start, Action<AppointmentFixture> verify)
        {
            Assert.Equal("Pacific Standard Time", TimeZoneInfo.Local.Id);
            var culture = CultureInfo.CurrentCulture;
            var style = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                DateFormatManager.Apply(DateStyle.Iso);
                string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:editor-nominal-duration\r\n" + start +
                    "DURATION:P1DT2H\r\nRRULE:FREQ=DAILY;COUNT=2\r\nSUMMARY:Nominal duration\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
                Assert.Equal(1, fixture.Store.ImportEvents(IcsService.ParseText(text).Events));
                fixture.Editor.Load(Assert.Single(fixture.Store.Events));
                fixture.View.EditWholeSeries = true;
                verify(fixture);
            }
            finally { CultureInfo.CurrentCulture = culture; DateFormatManager.Apply(style); }
        }

        private static void Reopen(AppointmentFixture fixture)
        {
            Assert.Null(fixture.Store.LoadError);
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);
            Assert.Null(fixture.Store.LoadError);
        }
    }
}
