using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class ReviewRoundZoneRegressionTests
    {
        // A zone some writers express as a seven-day BYMONTHDAY list with BYDAY: summer starts on
        // the second Sunday of March and ends on the last Sunday of October.
        private const string Zone =
            "BEGIN:VTIMEZONE\r\nTZID:Made Up Lists\r\nBEGIN:STANDARD\r\nDTSTART:19701025T030000\r\n" +
            "RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=SU;BYMONTHDAY=25,26,27,28,29,30,31\r\nTZOFFSETFROM:+0215\r\nTZOFFSETTO:+0115\r\nEND:STANDARD\r\n" +
            "BEGIN:DAYLIGHT\r\nDTSTART:19700308T020000\r\nRRULE:FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=8,9,10,11,12,13,14;BYDAY=SU\r\n" +
            "TZOFFSETFROM:+0115\r\nTZOFFSETTO:+0215\r\nEND:DAYLIGHT\r\nEND:VTIMEZONE";

        private static string Calendar(string day) =>
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + Zone + "\r\nBEGIN:VEVENT\r\nUID:lists@example.com\r\nSUMMARY:Call\r\n" +
            "DTSTART;TZID=Made Up Lists:" + day + "T090000\r\nDTEND;TZID=Made Up Lists:" + day + "T100000\r\n" +
            "END:VEVENT\r\nEND:VCALENDAR\r\n";

        [Theory]
        [InlineData("20260315", 6, 45)]   // after the second Sunday of March: summer
        [InlineData("20260305", 7, 45)]   // before it: winter
        [InlineData("20261030", 7, 45)]   // after the last Sunday of October: winter
        public void SevenDayMonthDayListWithWeekdayIsReadAsAnOrdinalWeekday(string day, int hour, int minute)
        {
            var ev = Assert.Single(IcsService.ParseText(Calendar(day)).Events);

            var date = DateTime.ParseExact(day, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Utc),
                ev.Start.ToUniversalTime());
        }
    }
}
