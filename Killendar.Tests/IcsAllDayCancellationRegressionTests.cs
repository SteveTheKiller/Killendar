using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-J (IE-005): Outlook cancels one date of an all-day series with a timed RECURRENCE-ID.
    [Collection("Calendar settings")]
    public sealed class IcsAllDayCancellationRegressionTests
    {
        private static string Calendar(string recurrenceId) =>
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
            "BEGIN:VEVENT\r\nUID:allday-series\r\nSUMMARY:Daily\r\nDTSTART;VALUE=DATE:20260302\r\nDTEND;VALUE=DATE:20260303\r\n" +
            "RRULE:FREQ=DAILY;COUNT=5\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:allday-series\r\nSUMMARY:Daily\r\n" + recurrenceId + "\r\nSTATUS:CANCELLED\r\n" +
            "DTSTART;VALUE=DATE:20260304\r\nDTEND;VALUE=DATE:20260305\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        [Theory]
        [InlineData("RECURRENCE-ID:20260304T000000Z")]
        [InlineData("RECURRENCE-ID;TZID=Europe/Berlin:20260304T000000")]
        public void ATimedCancellationOfAnAllDayDateSkipsThatDate(string recurrenceId)
        {
            var parsed = IcsService.ParseText(Calendar(recurrenceId));

            var series = Assert.Single(parsed.Events);
            Assert.Equal(1, parsed.Canceled);
            Assert.Contains(new DateTime(2026, 3, 4), series.SkipDates);
        }
    }
}
