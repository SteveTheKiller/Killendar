using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-J (IE-003): BYMONTHDAY and BYMONTH that restate the start keep the repeat.
    [Collection("Calendar settings")]
    public sealed class IcsLenientRecurrenceRegressionTests
    {
        private static IcsParseResult Parse(params string[] eventLines) => IcsService.ParseText(
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:lenient@example.com\r\nSUMMARY:Lenient\r\n" +
            string.Join("\r\n", eventLines) + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

        [Fact]
        public void MonthlyByMonthDayOnTheStartsOwnDayIsMonthly()
        {
            var parsed = Parse("DTSTART:20260315T120000Z", "DTEND:20260315T130000Z", "RRULE:FREQ=MONTHLY;INTERVAL=1;BYMONTHDAY=15");

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(RepeatFreq.Monthly, ev.Repeat);
            Assert.Equal(0, ev.RepeatMonthWeek);
            Assert.Equal(0, parsed.Repeating);
        }

        [Fact]
        public void YearlyByMonthAndMonthDayOnTheStartIsYearly()
        {
            var parsed = Parse("DTSTART;VALUE=DATE:20260315", "DTEND;VALUE=DATE:20260316", "RRULE:FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=15");

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(RepeatFreq.Yearly, ev.Repeat);
            Assert.Equal(0, parsed.Repeating);
        }

        [Fact]
        public void AMonthDayThatIsNotTheStartsIsStillCounted()
        {
            var parsed = Parse("DTSTART:20260315T120000Z", "DTEND:20260315T130000Z", "RRULE:FREQ=MONTHLY;BYMONTHDAY=20");

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(RepeatFreq.None, ev.Repeat);
            Assert.Equal(1, parsed.Repeating);
        }

        [Fact]
        public void AMonthThatIsNotTheStartsIsStillCounted()
        {
            var parsed = Parse("DTSTART;VALUE=DATE:20260315", "DTEND;VALUE=DATE:20260316", "RRULE:FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=15");

            Assert.Equal(RepeatFreq.None, Assert.Single(parsed.Events).Repeat);
            Assert.Equal(1, parsed.Repeating);
        }

        [Fact]
        public void YearlyByMonthDayAloneMeansEveryMonthAndIsCounted()
        {
            // RFC 5545: BYMONTHDAY on a yearly rule with no BYMONTH picks that day in every month.
            var parsed = Parse("DTSTART;VALUE=DATE:20260315", "DTEND;VALUE=DATE:20260316", "RRULE:FREQ=YEARLY;BYMONTHDAY=15");

            Assert.Equal(RepeatFreq.None, Assert.Single(parsed.Events).Repeat);
            Assert.Equal(1, parsed.Repeating);
        }
    }
}
