using System;
using System.Linq;
using System.Text.RegularExpressions;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-J (IE-006): an alarm left open must not swallow END:VEVENT into the kept lines.
    [Collection("Calendar settings")]
    public sealed class IcsUnclosedAlarmRegressionTests
    {
        private const string Truncated =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:a@example.com\r\nSUMMARY:Alarm\r\n" +
            "DTSTART:20260301T090000Z\r\nDTEND:20260301T100000Z\r\nBEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\n" +
            "END:VEVENT\r\nEND:VCALENDAR\r\n";

        private static int Count(string text, string line) => Regex.Matches(text, "^" + Regex.Escape(line) + "\r?$", RegexOptions.Multiline).Count;

        [Fact]
        public void TheAlarmIsClosedAndTheEventEndsOnce()
        {
            var parsed = IcsService.ParseText(Truncated);

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(new[] { "BEGIN:VALARM", "ACTION:DISPLAY", "TRIGGER:-PT15M", "END:VALARM" }, ev.KeptProperties);
            Assert.Equal(15, ev.ReminderMinutes);

            string ics = IcsService.BuildIcs(new[] { ev });
            Assert.Equal(1, Count(ics, "END:VEVENT"));
            Assert.Equal(1, Count(ics, "BEGIN:VALARM"));
            Assert.Equal(1, Count(ics, "END:VALARM"));
        }
    }
}
