using System;
using System.Globalization;
using System.Linq;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class AppointmentAmbiguousInstantRegressionTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(1)]
        public void WholeSeriesTitleEditPreservesEveryInstantAcrossRepeatedLocalHour(int hours)
        {
            Assert.Equal("Pacific Standard Time", TimeZoneInfo.Local.Id);
            var previousCulture = CultureInfo.CurrentCulture;
            var previousStyle = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                DateFormatManager.Apply(DateStyle.Iso);
                string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:ambiguous-editor\r\n" +
                    "DTSTART:20261031T083000Z\r\nDURATION:PT" + hours + "H\r\nRRULE:FREQ=DAILY;COUNT=4\r\n" +
                    "SUMMARY:Original\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
                Assert.Equal(1, fixture.Store.ImportEvents(IcsService.ParseText(ics).Events));
                Assert.Null(fixture.Store.LoadError);
                var master = Assert.Single(fixture.Store.Events);
                var originalAnchor = master.RepeatAnchor;
                var from = new DateTime(2026, 10, 30);
                var to = new DateTime(2026, 11, 5);
                DateTime[] expectedStarts = Enumerable.Range(0, 4)
                    .Select(day => new DateTime(2026, 10, 31, 8, 30, 0, DateTimeKind.Utc).AddDays(day)).ToArray();
                DateTime[] expectedEnds = expectedStarts.Select(start => start.AddHours(hours)).ToArray();
                var occurrence = fixture.Store.GetInRange(from, to).Single(row => row.Start.Date == new DateTime(2026, 11, 1));
                Assert.Equal(new DateTime(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc), occurrence.Start.ToUniversalTime());
                fixture.Editor.Load(occurrence);
                fixture.View.EditWholeSeries = true;
                fixture.View.FieldTitle = "Title changed";

                fixture.Editor.Save();

                Assert.Null(fixture.View.Error);
                Assert.Null(fixture.Store.LoadError);
                Assert.Equal(originalAnchor, Assert.Single(fixture.Store.Events).RepeatAnchor);
                Assert.Equal(expectedStarts, fixture.Store.GetInRange(from, to).Select(row => row.Start.ToUniversalTime()).ToArray());
                Assert.Equal(expectedEnds, fixture.Store.GetInRange(from, to).Select(row => row.End.ToUniversalTime()).ToArray());
                string path = fixture.Store.FilePath;
                fixture.Store.Close();
                fixture.Store.Open(path);
                Assert.Null(fixture.Store.LoadError);
                Assert.Equal(originalAnchor, Assert.Single(fixture.Store.Events).RepeatAnchor);
                Assert.Equal(expectedStarts, fixture.Store.GetInRange(from, to).Select(row => row.Start.ToUniversalTime()).ToArray());
                Assert.Equal(expectedEnds, fixture.Store.GetInRange(from, to).Select(row => row.End.ToUniversalTime()).ToArray());
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                DateFormatManager.Apply(previousStyle);
            }
        }
    }
}
