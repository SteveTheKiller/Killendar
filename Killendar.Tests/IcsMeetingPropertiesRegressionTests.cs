using System;
using System.IO;
using System.Linq;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-005: meeting details Killendar does not edit travel through unchanged, attendee answers
    // are not replaced with an invented NEEDS-ACTION, and anything still dropped is reported.
    [Collection("Calendar settings")]
    public sealed class IcsMeetingPropertiesRegressionTests
    {
        private static readonly string[] Kept =
        {
            "ORGANIZER;CN=\"Boss, The\":mailto:boss@example.com",
            "ATTENDEE;PARTSTAT=ACCEPTED;CN=Pat;ROLE=REQ-PARTICIPANT:mailto:pat@example.com",
            "ATTENDEE;PARTSTAT=TENTATIVE:mailto:sam@example.com",
            "TRANSP:TRANSPARENT",
            "CLASS:PRIVATE",
            "URL:https://meet.example.com/room/" + new string('a', 90),
            "SEQUENCE:3",
            "X-MICROSOFT-CDO-BUSYSTATUS:FREE",
            "BEGIN:VALARM",
            "ACTION:DISPLAY",
            "DESCRIPTION:Reminder",
            "TRIGGER:-PT15M",
            "END:VALARM",
        };

        [Fact]
        public void MeetingDetailsSurviveStorageEditAndExport()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.ImportEvents(IcsService.ParseText(Meeting(Kept)).Events);
            Reopen(fixture);
            var edited = Assert.Single(fixture.Store.Events).Clone();
            edited.Title = "Planning, renamed";
            fixture.Store.Update(edited);
            Reopen(fixture);

            var lines = Unfolded(IcsService.BuildIcs(fixture.Store.Events));

            foreach (string line in Kept) Assert.Contains(line, lines);
            Assert.DoesNotContain(lines, l => l.Contains("NEEDS-ACTION"));
            Assert.Equal(1, lines.Count(l => l.StartsWith("SEQUENCE:", StringComparison.Ordinal)));
            Assert.Equal(new[] { "pat@example.com", "sam@example.com" }, Assert.Single(fixture.Store.Events).Attendees);
        }

        [Fact]
        public void RemovedAttendeeTakesItsAnswerAlongAndANewOneIsInvited()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.ImportEvents(IcsService.ParseText(Meeting(Kept)).Events);
            var edited = Assert.Single(fixture.Store.Events).Clone();
            edited.Attendees = ["sam@example.com", "new@example.com"];
            fixture.Store.Update(edited);

            var lines = Unfolded(IcsService.BuildIcs(fixture.Store.Events));

            Assert.DoesNotContain(lines, l => l.Contains("pat@example.com"));
            Assert.Contains("ATTENDEE;PARTSTAT=TENTATIVE:mailto:sam@example.com", lines);
            Assert.Contains("ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:new@example.com", lines);
        }

        [Fact]
        public void AlarmPropertiesStillDoNotLeakIntoTheAppointment()
        {
            var ev = Assert.Single(IcsService.ParseText(Meeting(
                "DESCRIPTION:The meeting", "BEGIN:VALARM", "ACTION:EMAIL", "DESCRIPTION:Alarm text",
                "ATTENDEE:mailto:alarm@example.com", "TRIGGER:-PT5M", "END:VALARM")).Events);

            Assert.Equal("The meeting", ev.Description);
            Assert.Empty(ev.Attendees);
        }

        [Fact]
        public void DroppedDetailsAreCountedAndReported()
        {
            var parsed = IcsService.ParseText(Meeting("ATTACH:https://example.com/agenda.pdf", "GEO:37.38;-122.08"));

            Assert.Equal(1, parsed.LostDetails);
            Assert.Single(parsed.Events);

            using var fixture = new AppointmentFixture();
            string file = Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, "lossy.ics");
            File.WriteAllText(file, Meeting("ATTACH:https://example.com/agenda.pdf"));
            var host = new EnglishCalendarHost();
            new IcsTransfer(host, fixture.Store).Import(file);
            Assert.Equal("Imported 1 appointments. Some details Killendar does not store were left out of 1", host.Status);
        }

        [Fact]
        public void KeptDetailsDoNotCountAsLost()
        {
            Assert.Equal(0, IcsService.ParseText(Meeting(Kept)).LostDetails);
        }

        [Fact]
        public void NativeAppointmentsExportNoKeptLines()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Local", Start = new DateTime(2026, 10, 22, 9, 0, 0),
                End = new DateTime(2026, 10, 22, 10, 0, 0), Attendees = ["a@example.com"] });

            var lines = Unfolded(IcsService.BuildIcs(fixture.Store.Events));

            Assert.Contains("ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:a@example.com", lines);
            Assert.DoesNotContain(lines, l => l.StartsWith("ORGANIZER", StringComparison.Ordinal) || l == "BEGIN:VALARM");
        }

        private static string[] Unfolded(string ics) =>
            ics.Replace("\r\n ", "").Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        private static void Reopen(AppointmentFixture fixture)
        {
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);
        }

        private static string Meeting(params string[] extra) =>
            "BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:meeting@example.com\r\nSUMMARY:Planning\r\n" +
            "DTSTART:20261022T160000Z\r\nDTEND:20261022T170000Z\r\n" + string.Join("\r\n", extra) +
            "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    }
}
