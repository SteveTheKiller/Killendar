using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-006: RFC 5545 parameter values may be quoted and may then contain ':', ';' and ','.
    // None of those delimit anything inside quotes.
    [Collection("Calendar settings")]
    public sealed class IcsQuotedParameterRegressionTests
    {
        [Theory]
        [InlineData("ATTENDEE;CN=\"Team: West\":mailto:person@example.com", "person@example.com")]
        [InlineData("ATTENDEE;CN=\"Smith, Jane\";ROLE=REQ-PARTICIPANT:mailto:jane@example.com", "jane@example.com")]
        [InlineData("ATTENDEE;CN=\"Ops; on call\":mailto:ops@example.com", "ops@example.com")]
        [InlineData("ATTENDEE;CN=\"a:b;c,d\";PARTSTAT=ACCEPTED:MAILTO:mixed@example.com", "mixed@example.com")]
        public void QuotedAttendeeParametersKeepTheExactAddress(string attendee, string expected)
        {
            var parsed = IcsService.ParseText(Calendar(attendee));

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(expected, Assert.Single(ev.Attendees));
            Assert.Contains(attendee, IcsService.BuildIcs(parsed.Events).Replace("\r\n ", "")
                .Split(new[] { "\r\n" }, StringSplitOptions.None));
        }

        [Fact]
        public void QuotedSemicolonDoesNotInventATimeZoneParameter()
        {
            var parsed = IcsService.ParseText(
                "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:quoted-zone@example.com\r\nSUMMARY:Zone\r\n" +
                "DTSTART;TZID=America/New_York;X-NOTE=\"see;TZID=Nowhere/Fake\":20261022T090000\r\n" +
                "DTEND;TZID=America/New_York:20261022T100000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(new DateTime(2026, 10, 22, 13, 0, 0, DateTimeKind.Utc), ev.Start.ToUniversalTime());
            Assert.Equal("America/New_York", ev.RepeatZoneId);
        }

        [Fact]
        public void QuotedTimeZoneValueIsUnquoted()
        {
            var parsed = IcsService.ParseText(
                "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:quoted-tzid@example.com\r\nSUMMARY:Zone\r\n" +
                "DTSTART;TZID=\"America/New_York\":20261022T090000\r\n" +
                "DTEND;TZID=\"America/New_York\":20261022T100000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

            Assert.Equal(new DateTime(2026, 10, 22, 13, 0, 0, DateTimeKind.Utc),
                Assert.Single(parsed.Events).Start.ToUniversalTime());
        }

        private static string Calendar(string attendee) =>
            "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:quoted@example.com\r\nSUMMARY:Quoted\r\n" +
            "DTSTART:20261022T090000Z\r\nDTEND:20261022T100000Z\r\n" + attendee + "\r\n" +
            "END:VEVENT\r\nEND:VCALENDAR\r\n";
    }
}
