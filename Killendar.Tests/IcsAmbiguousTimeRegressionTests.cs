using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public class IcsAmbiguousTimeRegressionTests
    {
        [Fact]
        public void NewYorkRepeatedHourUsesFirstOccurrence()
        {
            var result = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;TZID=America/New_York:20261101T013000\r\nDTEND;TZID=America/New_York:20261101T023000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
            Assert.Equal(0, result.Unreadable);
            Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc),
                Assert.Single(result.Events).Start.ToUniversalTime());
        }

        [Theory]
        [InlineData("Europe/London", "20261025T013000", 2026, 10, 25, 0, 30)]
        [InlineData("America/New_York", "20260308T023000", 2026, 3, 8, 7, 30)]
        [InlineData("America/New_York", "20260115T090000", 2026, 1, 15, 14, 0)]
        public void SourceZoneRulesPreserveOtherRepeatedGapAndOrdinaryTimes(
            string zone, string stamp, int year, int month, int day, int hour, int minute)
        {
            var result = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;TZID=" +
                zone + ":" + stamp + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
            Assert.Equal(0, result.Unreadable);
            Assert.Equal(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc),
                Assert.Single(result.Events).Start.ToUniversalTime());
        }

        [Fact]
        public void AmbiguousRecurrenceIdHasSameIdentityAsFirstUtcOccurrence()
        {
            const string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:fall-back-series\r\nRECURRENCE-ID;TZID=America/New_York:20261101T013000\r\nDTSTART:20261101T080000Z\r\nDTEND:20261101T090000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var local = Assert.Single(IcsService.ParseText(text).Events);
            var utc = Assert.Single(IcsService.ParseText(text.Replace(
                "RECURRENCE-ID;TZID=America/New_York:20261101T013000", "RECURRENCE-ID:20261101T053000Z")).Events);
            Assert.Equal(utc.Id, local.Id);
            Assert.Equal(utc.OccurrenceStart, local.OccurrenceStart);
        }
    }
}
