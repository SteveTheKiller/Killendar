using System;
using System.Globalization;
using System.IO;
using Killendar.Features;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-015: a CSV value that is present but unreadable is reported, never replaced with a guess.
    // Only a blank optional value gets the documented default.
    [Collection("Calendar settings")]
    public sealed class CsvMalformedFieldRegressionTests
    {
        private const string Header = "Subject,Start Date,Start Time,End Date,End Time,All day event\r\n";

        [Theory]
        [InlineData("Bad start,2026-10-22,25:99,2026-10-22,10:00,FALSE")]
        [InlineData("Bad end time,2026-10-22,09:00,2026-10-22,noon-ish,FALSE")]
        [InlineData("Bad end date,2026-10-22,09:00,2026-13-45,10:00,FALSE")]
        [InlineData("Reversed,2026-10-22,10:00,2026-10-22,09:00,FALSE")]
        [InlineData("Reversed days,2026-10-22,09:00,2026-10-21,10:00,FALSE")]
        [InlineData("Bad all-day end,2026-10-22,,not a date,,TRUE")]
        [InlineData("Reversed all-day,2026-10-22,,2026-10-20,,TRUE")]
        public void MalformedSuppliedValueSkipsTheRow(string row)
        {
            var parsed = CsvService.ParseText(Header + row + "\r\n");

            Assert.Empty(parsed.Events);
            Assert.Equal(1, parsed.Malformed);
        }

        [Theory]
        [InlineData("Blank times,2026-10-22,,,,FALSE", 0, 1)]
        [InlineData("Blank end,2026-10-22,09:00,,,FALSE", 9, 10)]
        [InlineData("End time only,2026-10-22,09:00,,11:30,FALSE", 9, 11.5)]
        public void BlankOptionalValuesKeepTheirDefaults(string row, double startHour, double endHour)
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                var parsed = CsvService.ParseText(Header + row + "\r\n");

                var ev = Assert.Single(parsed.Events);
                Assert.Equal(0, parsed.Malformed);
                Assert.Equal(new DateTime(2026, 10, 22).AddHours(startHour), ev.Start);
                Assert.Equal(new DateTime(2026, 10, 22).AddHours(endHour), ev.End);
            }
            finally { CultureInfo.CurrentCulture = culture; }
        }

        [Fact]
        public void ImportStatusNamesMalformedRows()
        {
            using var fixture = new AppointmentFixture();
            string file = Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, "rows.csv");
            File.WriteAllText(file, Header + "Good,2026-10-22,09:00,2026-10-22,10:00,FALSE\r\n" +
                "Bad,2026-10-22,25:99,2026-10-22,10:00,FALSE\r\n");
            var host = new EnglishCalendarHost();

            new IcsTransfer(host, fixture.Store).Import(file);

            Assert.Single(fixture.Store.Events);
            Assert.Equal("Imported 1 appointments. 1 had a date or time that could not be read and were skipped", host.Status);
        }
    }
}
