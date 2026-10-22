using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S17: meeting details Killendar keeps from imports, shown and used safely.
    [Collection("Calendar settings")]
    public sealed class MeetingInfoRegressionTests
    {
        private static CalendarEvent Imported(params string[] kept) => new()
        {
            Title = "Planning", Start = new DateTime(2026, 10, 22, 9, 0, 0), End = new DateTime(2026, 10, 22, 10, 0, 0),
            KeptProperties = [.. kept],
        };

        [Fact]
        public void ReadsOrganizerRepliesAndPrivacy()
        {
            var info = MeetingInfo.From(Imported(
                "ORGANIZER;CN=\"Boss, The\":mailto:boss@example.com",
                "ATTENDEE;CN=Pat;PARTSTAT=ACCEPTED:mailto:pat@example.com",
                "ATTENDEE;PARTSTAT=DECLINED;CN=Sam:mailto:sam@example.com",
                "ATTENDEE;PARTSTAT=TENTATIVE:mailto:kim@example.com",
                "ATTENDEE:mailto:lee@example.com",
                "CLASS:PRIVATE"));

            Assert.NotNull(info);
            Assert.Equal("Boss, The", info!.OrganizerName);
            Assert.Equal("boss@example.com", info.OrganizerAddress);
            Assert.Equal([MeetingReply.Accepted, MeetingReply.Declined, MeetingReply.Tentative, MeetingReply.NeedsAction],
                info.Attendees.Select(a => a.Reply));
            Assert.Equal("Pat", info.Attendees[0].Name);
            Assert.True(info.Private);
        }

        [Fact]
        public void PlainAppointmentHasNoMeeting()
            => Assert.Null(MeetingInfo.From(Imported()));

        [Theory]
        [InlineData("URL:https://meet.example.com/abc", "https://meet.example.com/abc")]
        [InlineData("X-MICROSOFT-SKYPETEAMSMEETINGURL:https://teams.microsoft.com/l/meetup-join/x", "https://teams.microsoft.com/l/meetup-join/x")]
        [InlineData("URL:javascript:alert(1)", null)]
        [InlineData("URL:file:///C:/Windows/System32/calc.exe", null)]
        [InlineData(@"URL:\\server\share\run.exe", null)]
        public void OnlyWebLinksCanBeJoined(string kept, string? link)
            => Assert.Equal(link, MeetingInfo.From(Imported(kept, "ORGANIZER:mailto:a@example.com"))!.Link);

        [Fact]
        public void FindsAMeetingLinkInTheLocation()
        {
            var ev = Imported("ORGANIZER:mailto:a@example.com");
            ev.Location = "Online: https://zoom.us/j/123456?pwd=x and dial-in";
            Assert.Equal("https://zoom.us/j/123456?pwd=x", MeetingInfo.From(ev)!.Link);
        }

        [Fact]
        public void ShowAsFreeWritesTheStandardLineAndRoundTrips()
        {
            var ev = Imported();
            MeetingInfo.SetFree(ev, true);
            Assert.True(MeetingInfo.IsFree(ev));
            string ics = IcsService.BuildIcs([ev]);
            Assert.Contains("TRANSP:TRANSPARENT", ics);
            Assert.True(MeetingInfo.IsFree(Assert.Single(IcsService.ParseText(ics).Events)));

            MeetingInfo.SetFree(ev, false);
            Assert.False(MeetingInfo.IsFree(ev));
        }

        [Fact]
        public void EditorSavesShowAsFree()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldTitle = "Focus";
            fixture.View.FieldShowAsFree = true;

            fixture.Editor.Save();

            Assert.True(MeetingInfo.IsFree(Assert.Single(fixture.Store.Events)));
        }
    }
}
