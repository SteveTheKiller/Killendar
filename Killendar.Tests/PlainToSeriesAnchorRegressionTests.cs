using System;
using System.Globalization;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-001: an imported one-off appointment keeps its source clock. When the user moves it and
    // makes it repeat in the same save, the series has to start where the user put it, not where
    // the import originally had it.
    [Collection("Calendar settings")]
    public sealed class PlainToSeriesAnchorRegressionTests
    {
        [Theory]
        [InlineData("DTSTART:20261022T160000Z\r\nDTEND:20261022T170000Z", 18)]
        [InlineData("DTSTART;TZID=America/New_York:20261022T090000\r\nDTEND;TZID=America/New_York:20261022T100000", 15)]
        public void MovedImportBecomingDailyStartsAtTheEditedInstant(string times, int targetUtcHour)
        {
            var culture = CultureInfo.CurrentCulture;
            var style = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                DateFormatManager.Apply(DateStyle.Iso);
                fixture.Store.ImportEvents(IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n" +
                    "UID:plain-to-series@example.com\r\nSUMMARY:Plain\r\n" + times + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n").Events);
                var target = new DateTime(2026, 10, 23, targetUtcHour, 0, 0, DateTimeKind.Utc);
                var local = target.ToLocalTime();

                fixture.Editor.Load(Assert.Single(fixture.Store.Events));
                fixture.View.FieldStartDate = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                fixture.View.FieldStartTime = local.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
                fixture.View.FieldRepeat = RepeatFreq.Daily;
                fixture.View.FieldRepeatCount = 3;
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);

                string path = fixture.Store.FilePath;
                fixture.Store.Close();
                fixture.Store.Open(path);

                var starts = fixture.Store.GetInRange(new DateTime(2026, 10, 20), new DateTime(2026, 10, 30))
                    .Select(e => e.Start.ToUniversalTime()).ToArray();
                Assert.Equal(new[] { target, target.AddDays(1), target.AddDays(2) }, starts);

                var exported = IcsService.ParseText(IcsService.BuildIcs(fixture.Store.Events)).Events;
                Assert.Equal(target, Assert.Single(exported).Start.ToUniversalTime());
            }
            finally { CultureInfo.CurrentCulture = culture; DateFormatManager.Apply(style); }
        }
    }
}
