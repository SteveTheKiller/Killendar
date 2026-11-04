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

        // The same holds for a timed EXDATE or RDATE on an all-day series: the date is the one the
        // value names on its own clock. Converting to this computer's time first moved it a day
        // early west of UTC (and the Berlin form a day early at UTC too).
        [Theory]
        [InlineData("20260304T000000Z")]
        [InlineData(";TZID=Europe/Berlin:20260304T000000")]
        public void ATimedExclusionOrExtraDateOnAnAllDaySeriesKeepsItsOwnDate(string value)
        {
            string sep = value.StartsWith(";", StringComparison.Ordinal) ? "" : ":";
            var parsed = IcsService.ParseText(
                "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
                "BEGIN:VEVENT\r\nUID:allday-exdate\r\nSUMMARY:Daily\r\nDTSTART;VALUE=DATE:20260302\r\nDTEND;VALUE=DATE:20260303\r\n" +
                "RRULE:FREQ=WEEKLY;COUNT=5\r\nEXDATE" + sep + value + "\r\nRDATE" + sep + value.Replace("0304", "0305") + "\r\n" +
                "END:VEVENT\r\nEND:VCALENDAR\r\n");

            var series = Assert.Single(parsed.Events);
            Assert.Contains(new DateTime(2026, 3, 4), series.SkipDates);
            Assert.Contains(new DateTime(2026, 3, 5), series.ExtraDates);
        }
    }
}
