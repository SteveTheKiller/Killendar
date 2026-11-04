using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-J (IE-004): a timed start with a DATE UNTIL keeps its repeat through that day.
    [Collection("Calendar settings")]
    public sealed class IcsDateUntilRegressionTests
    {
        private static IcsParseResult Parse(params string[] eventLines) => IcsService.ParseText(
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:until@example.com\r\nSUMMARY:Until\r\n" +
            string.Join("\r\n", eventLines) + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

        [Fact]
        public void ATimedStartWithADateUntilKeepsItsRepeatThroughThatDay()
        {
            // 5 January 2026 is a Monday; weekly through Monday 30 March is 13 dates.
            var parsed = Parse("DTSTART:20260105T120000Z", "DTEND:20260105T130000Z", "RRULE:FREQ=WEEKLY;UNTIL=20260330");

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(RepeatFreq.Weekly, ev.Repeat);
            Assert.Equal(0, parsed.Repeating);
            var dates = Recurrence.Expand(ev, new DateTime(2026, 1, 1), new DateTime(2026, 5, 1))
                .Select(o => o.Start.ToUniversalTime().Date).ToList();
            Assert.Equal(13, dates.Count);
            Assert.Contains(new DateTime(2026, 3, 30), dates);
            Assert.DoesNotContain(new DateTime(2026, 4, 6), dates);
        }

        [Fact]
        public void AZonedLateEveningSeriesKeepsItsLastDateOnItsOwnClock()
        {
            // 23:00 in Berlin each Monday, through Monday 30 March 2026 on Berlin's clock.
            var parsed = Parse("DTSTART;TZID=Europe/Berlin:20260105T230000", "DTEND;TZID=Europe/Berlin:20260105T233000",
                "RRULE:FREQ=WEEKLY;UNTIL=20260330");

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(RepeatFreq.Weekly, ev.Repeat);
            var berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
            var dates = Recurrence.Expand(ev, new DateTime(2026, 1, 1), new DateTime(2026, 5, 1))
                .Select(o => TimeZoneInfo.ConvertTimeFromUtc(o.Start.ToUniversalTime(), berlin)).ToList();
            Assert.Equal(13, dates.Count);
            Assert.Equal(new DateTime(2026, 3, 30, 23, 0, 0), dates.Last());
        }

        [Fact]
        public void AFloatingStartWithADateUntilKeepsItsRepeatThroughThatDay()
        {
            var parsed = Parse("DTSTART:20260105T120000", "DTEND:20260105T130000", "RRULE:FREQ=WEEKLY;UNTIL=20260330");

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(RepeatFreq.Weekly, ev.Repeat);
            Assert.Equal(0, parsed.Repeating);
            var dates = Recurrence.Expand(ev, new DateTime(2026, 1, 1), new DateTime(2026, 5, 1)).Select(o => o.Start.Date).ToList();
            Assert.Equal(13, dates.Count);
            Assert.Contains(new DateTime(2026, 3, 30), dates);
        }
    }
}
