using System.Windows;
using Killendar.Features;

// MainWindow's side of the appointment panel: it satisfies IAppointmentView by forwarding to the
// boxes in MainWindow.xaml, and routes the panel's buttons to the editor. The validation and the
// store writes live in Features/Appointments/AppointmentEditor.cs.
namespace Killendar.Shell
{
    public partial class MainWindow : IAppointmentView
    {
        private AppointmentEditor _appointments = null!;

        string IAppointmentView.FieldTitle       { get => FieldTitle.Text;       set => FieldTitle.Text = value; }
        string IAppointmentView.FieldLocation    { get => FieldLocation.Text;    set => FieldLocation.Text = value; }
        string IAppointmentView.FieldDescription { get => FieldDescription.Text; set => FieldDescription.Text = value; }
        string IAppointmentView.FieldAttendees   { get => FieldAttendees.Text;   set => FieldAttendees.Text = value; }
        string IAppointmentView.FieldStartDate   { get => FieldStartDate.Text;   set => FieldStartDate.Text = value; }
        string IAppointmentView.FieldStartTime   { get => FieldStartTime.Text;   set => FieldStartTime.Text = value; }
        string IAppointmentView.FieldEndDate     { get => FieldEndDate.Text;     set => FieldEndDate.Text = value; }
        string IAppointmentView.FieldEndTime     { get => FieldEndTime.Text;     set => FieldEndTime.Text = value; }

        string IAppointmentView.Heading { set => SidebarTitle.Text = value; }

        bool IAppointmentView.CanDelete
        {
            set => DeleteAppointmentBtn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        string IAppointmentView.DateHint
        {
            set
            {
                FieldStartDate.ToolTip = value;
                FieldEndDate.ToolTip = value;
            }
        }

        void IAppointmentView.SetAllDay(string caption, bool timesVisible)
        {
            AllDayToggle.Content = caption;
            // Hidden, not Collapsed: the boxes keep their space so the rows do not jump.
            var vis = timesVisible ? Visibility.Visible : Visibility.Hidden;
            FieldStartTime.Visibility = vis;
            FieldEndTime.Visibility = vis;
        }

        void IAppointmentView.ShowError(string message)
        {
            SidebarError.Text = message;
            SidebarError.Visibility = Visibility.Visible;
        }

        void IAppointmentView.ClearError() => ClearSidebarError();

        void IAppointmentView.Focus(AppointmentField field, bool selectAll)
        {
            var box = field switch
            {
                AppointmentField.StartDate => FieldStartDate,
                AppointmentField.StartTime => FieldStartTime,
                AppointmentField.EndDate   => FieldEndDate,
                AppointmentField.EndTime   => FieldEndTime,
                _                          => FieldTitle,
            };
            box.Focus();
            if (selectAll) box.SelectAll();
        }

        void IAppointmentView.OpenPanel() => OpenSidebarPanel();

        void IAppointmentView.ClosePanel() => CloseSidebar();

        // ---- panel buttons ----

        private void SidebarClose_Click(object sender, RoutedEventArgs e) => CloseSidebar();

        private void AllDayToggle_Click(object sender, RoutedEventArgs e) => _appointments.ToggleAllDay();

        private void SaveAppointment_Click(object sender, RoutedEventArgs e) => _appointments.Save();

        private void DeleteAppointment_Click(object sender, RoutedEventArgs e) => _appointments.Delete();

        /// <summary>Opens the panel composing a new appointment at the given time.</summary>
        private void OpenNewAppointment(System.DateTime when) => _appointments.NewAt(when);

        /// <summary>Opens the panel on an existing appointment.</summary>
        private void OpenAppointment(Models.CalendarEvent ev) => _appointments.Load(ev);
    }
}
