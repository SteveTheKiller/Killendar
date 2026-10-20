using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public class ImportDateBoundsRegressionTests
    {
        [Theory]
        [InlineData("Bad,9999-12-31,,9999-12-31,,TRUE")]
        [InlineData("Bad,9999-12-31,,,,TRUE")]
        [InlineData("Bad,9999-12-31,23:30:00,,,FALSE")]
        public void CsvUnrepresentableEndSkipsOnlyBadRecord(string badRow)
        {
            var result = CsvService.ParseText("Subject,Start Date,Start Time,End Date,End Time,All day event\r\n" +
                badRow + "\r\nNormal,2026-10-20,,2026-10-20,,TRUE\r\n");
            Assert.True(result.HeaderValid);
            Assert.Equal(1, result.Unreadable);
            var retained = Assert.Single(result.Events);
            Assert.Equal("Normal", retained.Title);
            Assert.Equal(new DateTime(2026, 10, 20), retained.Start);
            Assert.Equal(new DateTime(2026, 10, 21), retained.End);
        }

        [Theory]
        [InlineData("DTSTART;VALUE=DATE:99991231")]
        [InlineData("DTSTART:99991231T233000")]
        public void IcsUnrepresentableEndSkipsOnlyBadRecord(string badStart)
        {
            string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Bad\r\n" + badStart +
                "\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nSUMMARY:Normal\r\nDTSTART;VALUE=DATE:20261020\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var result = IcsService.ParseText(text);
            Assert.Equal(1, result.Unreadable);
            var retained = Assert.Single(result.Events);
            Assert.Equal("Normal", retained.Title);
            Assert.Equal(new DateTime(2026, 10, 20), retained.Start);
            Assert.Equal(new DateTime(2026, 10, 21), retained.End);
        }
    }
}
