using System;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class IcsServiceRegressionTests
    {
        [Fact]
        public void TextUidConnectsMasterAndOverrideThroughRoundTrip()
        {
            const string ics = "BEGIN:VCALENDAR\r\n" +
                "BEGIN:VEVENT\r\n" +
                "RECURRENCE-ID:20261021T090000\r\n" +
                "DTSTART:20261021T110000\r\n" +
                "DTEND:20261021T120000\r\n" +
                "UID:team-standup@example.com\r\n" +
                "SUMMARY:Moved standup\r\n" +
                "END:VEVENT\r\n" +
                "BEGIN:VEVENT\r\n" +
                "DTSTART:20261020T090000\r\n" +
                "DTEND:20261020T100000\r\n" +
                "RRULE:FREQ=DAILY;COUNT=3\r\n" +
                "UID:team-standup@example.com\r\n" +
                "SUMMARY:Standup\r\n" +
                "END:VEVENT\r\n" +
                "END:VCALENDAR\r\n";

            var parsed = IcsService.ParseText(ics);
            Assert.Equal(2, parsed.Events.Count);
            var master = Assert.Single(parsed.Events, e => e.IsSeries);
            var moved = Assert.Single(parsed.Events, e => e.IsOverride);
            Assert.Equal(master.Id, moved.SeriesId);
            Assert.NotEqual(master.Id, moved.Id);
            Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0), moved.OccurrenceStart);
            Assert.Equal(master.Id, Assert.Single(IcsService.ParseText(ics).Events, e => e.IsSeries).Id);

            var again = IcsService.ParseText(IcsService.BuildIcs(parsed.Events));
            var roundTripMaster = Assert.Single(again.Events, e => e.IsSeries);
            var roundTripOverride = Assert.Single(again.Events, e => e.IsOverride);
            Assert.Equal(roundTripMaster.Id, roundTripOverride.SeriesId);
        }

        [Theory]
        [InlineData("America/New_York", "20260115T090000", 2026, 1, 15, 14)]
        [InlineData("America/New_York", "20260715T090000", 2026, 7, 15, 13)]
        [InlineData("Eastern Standard Time", "20260715T090000", 2026, 7, 15, 13)]
        public void TzidUsesTheSourceZonesOffsetIncludingDaylightTime(
            string zone, string stamp, int year, int month, int day, int utcHour)
        {
            string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n" +
                $"DTSTART;TZID={zone}:{stamp}\r\n" +
                $"DTEND;TZID={zone}:{year:D4}{month:D2}{day:D2}T100000\r\n" +
                "SUMMARY:Meeting\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

            var ev = Assert.Single(IcsService.ParseText(ics).Events);
            var expectedUtc = new DateTime(year, month, day, utcHour, 0, 0, DateTimeKind.Utc);
            Assert.Equal(expectedUtc.ToLocalTime(), ev.Start);
            Assert.Equal(expectedUtc.AddHours(1).ToLocalTime(), ev.End);
        }

        [Fact]
        public void UnknownTzidReportsTheZoneInsteadOfGuessingLocalTime()
        {
            const string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n" +
                "DTSTART;TZID=Mars/Colony:20260715T090000\r\n" +
                "DTEND;TZID=Mars/Colony:20260715T100000\r\n" +
                "SUMMARY:Meeting\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

            var error = Assert.Throws<TimeZoneNotFoundException>(() => IcsService.ParseText(ics));
            Assert.Contains("Mars/Colony", error.Message);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DurationIsAppliedAfterStartRegardlessOfPropertyOrder(bool durationFirst)
        {
            string properties = durationFirst
                ? "DURATION:PT2H\r\nDTSTART:20261020T090000\r\n"
                : "DTSTART:20261020T090000\r\nDURATION:PT2H\r\n";
            string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n" + properties +
                "SUMMARY:Long meeting\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

            var ev = Assert.Single(IcsService.ParseText(ics).Events);
            Assert.Equal(TimeSpan.FromHours(2), ev.End - ev.Start);
        }

        [Theory]
        [InlineData("P99999999999999W")]
        [InlineData("P2147483647W")]
        [InlineData("P999999999D")]
        public void OversizedDurationSkipsOnlyTheBadEvent(string duration)
        {
            string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n" +
                "DTSTART:20261020T090000\r\nDURATION:" + duration + "\r\n" +
                "SUMMARY:Bad event\r\nEND:VEVENT\r\n" +
                "BEGIN:VEVENT\r\nDTSTART:20261021T090000\r\n" +
                "DTEND:20261021T100000\r\nSUMMARY:Good event\r\n" +
                "END:VEVENT\r\nEND:VCALENDAR\r\n";

            var parsed = IcsService.ParseText(ics);
            Assert.Equal(1, parsed.Unreadable);
            Assert.Equal("Good event", Assert.Single(parsed.Events).Title);
        }
    }
}
