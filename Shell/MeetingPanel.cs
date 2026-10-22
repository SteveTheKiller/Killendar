using System.Windows;
using System.Windows.Controls;
using Killendar.Features;
using Killendar.Services;

// The appointment panel's meeting section (S17): who organized an imported invitation, who
// answered what, whether it is private, and a Join button for an ordinary web link. Opening the
// link is always the user's click; MeetingInfo only offers http and https addresses.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private string? _meetingLink;

        bool IAppointmentView.FieldShowAsFree
        {
            get => ShowAsFreeToggle.IsChecked == true;
            set => ShowAsFreeToggle.IsChecked = value;
        }

        void IAppointmentView.ShowMeeting(MeetingInfo? meeting)
        {
            MeetingRows.Children.Clear();
            _meetingLink = meeting?.Link;
            MeetingPanel.Visibility = meeting == null ? Visibility.Collapsed : Visibility.Visible;
            JoinMeetingBtn.Visibility = _meetingLink == null ? Visibility.Collapsed : Visibility.Visible;
            if (meeting == null) return;

            string organizer = meeting.OrganizerName.Length > 0
                ? meeting.OrganizerName + (meeting.OrganizerAddress.Length > 0 ? " <" + meeting.OrganizerAddress + ">" : "")
                : meeting.OrganizerAddress;
            if (organizer.Length > 0) AddMeetingRow(string.Format(Loc("Str_Meet_Organizer"), organizer), "TextBrush");
            if (meeting.Private) AddMeetingRow(Loc("Str_Meet_Private"), "MutedTextBrush");
            foreach (var attendee in meeting.Attendees)
            {
                string who = attendee.Name.Length > 0 ? attendee.Name : attendee.Address;
                string reply = Loc(attendee.Reply switch
                {
                    MeetingReply.Accepted => "Str_Meet_Accepted",
                    MeetingReply.Declined => "Str_Meet_Declined",
                    MeetingReply.Tentative => "Str_Meet_Tentative",
                    MeetingReply.Delegated => "Str_Meet_Delegated",
                    _ => "Str_Meet_NoReply",
                });
                AddMeetingRow(who + "    " + reply, "MutedTextBrush");
            }
        }

        private void AddMeetingRow(string text, string brush)
        {
            var row = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            row.SetResourceReference(TextBlock.ForegroundProperty, brush);
            MeetingRows.Children.Add(row);
        }

        private void JoinMeeting_Click(object sender, RoutedEventArgs e)
        {
            if (_meetingLink != null && MeetingInfo.SafeLink(_meetingLink)) WebLink.Open(_meetingLink);
        }
    }
}
