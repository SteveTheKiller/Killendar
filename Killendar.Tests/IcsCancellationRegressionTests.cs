using System;
using System.IO;
using System.Linq;
using Killendar.Features;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-002: a canceled meeting must never arrive as an ordinary appointment.
    [Collection("Calendar settings")]
    public sealed class IcsCancellationRegressionTests
    {
        [Theory]
        [InlineData("METHOD:CANCEL\r\n", "STATUS:CANCELLED\r\n")]
        [InlineData("METHOD:CANCEL\r\n", "")]
        [InlineData("METHOD:PUBLISH\r\n", "STATUS:CANCELLED\r\n")]
        public void CanceledMeetingIsCountedNotImported(string method, string status)
        {
            var parsed = IcsService.ParseText("BEGIN:VCALENDAR\r\n" + method + "BEGIN:VEVENT\r\n" +
                "UID:canceled@example.com\r\nSUMMARY:Canceled meeting\r\n" + status +
                "DTSTART:20261022T090000Z\r\nDTEND:20261022T100000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

            Assert.Empty(parsed.Events);
            Assert.Equal(1, parsed.Canceled);
        }

        [Fact]
        public void ConfirmedMeetingStillImports()
        {
            var parsed = IcsService.ParseText("BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\n" +
                "UID:confirmed@example.com\r\nSUMMARY:Meeting\r\nSTATUS:CONFIRMED\r\n" +
                "DTSTART:20261022T090000Z\r\nDTEND:20261022T100000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

            Assert.Single(parsed.Events);
            Assert.Equal(0, parsed.Canceled);
        }

        [Fact]
        public void CanceledOccurrenceRemovesThatDateFromItsSeries()
        {
            using var fixture = new AppointmentFixture();
            var parsed = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n" +
                "UID:series@example.com\r\nSUMMARY:Standup\r\nDTSTART:20261019T160000Z\r\nDTEND:20261019T161500Z\r\n" +
                "RRULE:FREQ=DAILY;COUNT=5\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\n" +
                "UID:series@example.com\r\nRECURRENCE-ID:20261021T160000Z\r\nSTATUS:CANCELLED\r\nSUMMARY:Standup\r\n" +
                "DTSTART:20261021T160000Z\r\nDTEND:20261021T161500Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

            Assert.Equal(1, parsed.Canceled);
            fixture.Store.ImportEvents(parsed.Events);
            var starts = fixture.Store.GetInRange(new DateTime(2026, 10, 18), new DateTime(2026, 10, 25))
                .Select(e => e.Start.ToUniversalTime().Day).ToArray();
            Assert.Equal(new[] { 19, 20, 22, 23 }, starts);
        }

        [Fact]
        public void ImportStatusNamesCanceledMeetings()
        {
            using var fixture = new AppointmentFixture();
            string file = Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, "cancel.ics");
            File.WriteAllText(file, "BEGIN:VCALENDAR\r\nMETHOD:CANCEL\r\nBEGIN:VEVENT\r\nUID:canceled@example.com\r\n" +
                "SUMMARY:Canceled\r\nSTATUS:CANCELLED\r\nDTSTART:20261022T090000Z\r\nDTEND:20261022T100000Z\r\n" +
                "END:VEVENT\r\nEND:VCALENDAR\r\n");
            var host = new EnglishCalendarHost();

            new IcsTransfer(host, fixture.Store).Import(file);

            Assert.Empty(fixture.Store.Events);
            Assert.Equal("Imported 0 appointments. 1 were canceled meetings and were not added", host.Status);
        }
    }
}
