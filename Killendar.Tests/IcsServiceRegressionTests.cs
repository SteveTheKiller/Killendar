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
    }
}
