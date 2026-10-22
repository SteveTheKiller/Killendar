using System;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-D: a calendar file's own VTIMEZONE under a name Windows does not know.
    [Collection("Calendar settings")]
    public sealed class CustomTimeZoneImportTests
    {
        private static string Calendar(string zone, params string[] eventLines)
        {
            var all = new System.Collections.Generic.List<string> { "BEGIN:VCALENDAR", "VERSION:2.0" };
            if (zone.Length > 0) all.AddRange(zone.Split('\n'));
            all.AddRange(new[] { "BEGIN:VEVENT", "UID:zone@example.com", "SUMMARY:Call" });
            all.AddRange(eventLines);
            all.AddRange(new[] { "END:VEVENT", "END:VCALENDAR", "" });
            return string.Join("\r\n", all);
        }

        // Outlook's name for a zone someone set up by hand, with US Eastern rules.
        private const string Eastern =
            "BEGIN:VTIMEZONE\nTZID:Customized Time Zone\nBEGIN:STANDARD\nDTSTART:16011104T020000\n" +
            "RRULE:FREQ=YEARLY;BYDAY=1SU;BYMONTH=11\nTZOFFSETFROM:-0400\nTZOFFSETTO:-0500\nEND:STANDARD\n" +
            "BEGIN:DAYLIGHT\nDTSTART:16010311T020000\nRRULE:FREQ=YEARLY;BYDAY=2SU;BYMONTH=3\nTZOFFSETFROM:-0500\n" +
            "TZOFFSETTO:-0400\nEND:DAYLIGHT\nEND:VTIMEZONE";

        // No Windows zone keeps +01:15 in winter and +02:15 in summer.
        private const string Odd =
            "BEGIN:VTIMEZONE\nTZID:Made Up\nBEGIN:STANDARD\nDTSTART:19701025T030000\n" +
            "RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10\nTZOFFSETFROM:+0215\nTZOFFSETTO:+0115\nEND:STANDARD\n" +
            "BEGIN:DAYLIGHT\nDTSTART:19700329T020000\nRRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=3\nTZOFFSETFROM:+0115\n" +
            "TZOFFSETTO:+0215\nEND:DAYLIGHT\nEND:VTIMEZONE";

        [Fact]
        public void AZoneWindowsAlsoHasIsUsedUnderItsWindowsName()
        {
            var ev = Assert.Single(IcsService.ParseText(Calendar(Eastern,
                "DTSTART;TZID=\"Customized Time Zone\":20261103T090000", "DTEND;TZID=\"Customized Time Zone\":20261103T100000",
                "RRULE:FREQ=WEEKLY;COUNT=10")).Events);

            Assert.Equal(new DateTime(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc), ev.Start.ToUniversalTime());
            Assert.Equal(RecurrenceTimeBasis.SourceZone, ev.RepeatTimeBasis);
            var zone = TimeZones.Find(ev.RepeatZoneId)!;
            Assert.NotNull(zone);
            Assert.Equal(TimeSpan.FromHours(-4), zone.GetUtcOffset(new DateTime(2027, 7, 1, 12, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(TimeSpan.FromHours(-5), zone.GetUtcOffset(new DateTime(2027, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
        }

        [Fact]
        public void AZoneNoWindowsZoneMatchesStillPlacesASingleAppointmentExactly()
        {
            var result = IcsService.ParseText(Calendar(Odd,
                "DTSTART;TZID=Made Up:20260715T090000", "DTEND;TZID=Made Up:20260715T100000"));

            var ev = Assert.Single(result.Events);
            Assert.Equal(new DateTime(2026, 7, 15, 6, 45, 0, DateTimeKind.Utc), ev.Start.ToUniversalTime());   // summer: +02:15
            Assert.Equal(0, result.ZoneApproximated);
        }

        [Fact]
        public void ARepeatOnAnUnmatchedZoneIsCounted()
        {
            var result = IcsService.ParseText(Calendar(Odd,
                "DTSTART;TZID=Made Up:20260115T090000", "DTEND;TZID=Made Up:20260115T100000", "RRULE:FREQ=WEEKLY;COUNT=40"));

            var ev = Assert.Single(result.Events);
            Assert.Equal(new DateTime(2026, 1, 15, 7, 45, 0, DateTimeKind.Utc), ev.Start.ToUniversalTime());   // winter: +01:15
            Assert.Equal(RepeatFreq.Weekly, ev.Repeat);
            Assert.Equal(1, result.ZoneApproximated);
        }

        [Fact]
        public void AnUnknownZoneWithNoDefinitionIsStillRefused()
        {
            Assert.ThrowsAny<Exception>(() => IcsService.ParseText(Calendar("",
                "DTSTART;TZID=Nowhere:20260715T090000", "DTEND;TZID=Nowhere:20260715T100000")));
        }

        [Fact]
        public void TheZoneReaderFollowsGapsAndRepeatedHours()
        {
            var zone = IcsCustomZone.Read([.. Odd.Split('\n')])!;
            // 29 March 2026: the clocks skip 02:00 to 03:00, read with the winter offset.
            Assert.Equal(new DateTime(2026, 3, 29, 1, 15, 0, DateTimeKind.Utc), zone.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0)));
            // 25 October 2026: 02:30 happens twice; the first is on the summer offset.
            Assert.Equal(new DateTime(2026, 10, 25, 0, 15, 0, DateTimeKind.Utc), zone.ToUtc(new DateTime(2026, 10, 25, 2, 30, 0)));
            Assert.True(zone.ChangesOffset);
            Assert.Null(zone.MatchSystemZone());
        }
    }
}
