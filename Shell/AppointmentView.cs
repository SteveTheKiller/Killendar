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

        // Backed by the chip row rather than a box - see BuildCategoryChips below.
        string IAppointmentView.FieldCategories  { get => ReadCategoryChips();   set => BuildCategoryChips(value); }
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
            // The localized strings predate the real checkbox and carry their own ASCII box.
            // Keep the translated words but let the themed CheckBox draw the state.
            AllDayToggle.Content = caption.StartsWith("[x] ") || caption.StartsWith("[ ] ")
                ? caption.Substring(4) : caption;
            AllDayToggle.IsChecked = !timesVisible;
            FieldStartLabel.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty,
                timesVisible ? "Str_Fld_Starts" : "Str_Fld_From");
            FieldEndLabel.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty,
                timesVisible ? "Str_Fld_Ends" : "Str_Fld_Through");
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

        void IAppointmentView.OpenPanel()
        {
            // The editor always claims the panel's content rows - a stale day agenda must never
            // sit behind a form that is about to open (DayAgendaPanel.cs).
            SidebarMode(agenda: false);
            OpenSidebarPanel();
        }

        // Routed, not straight to CloseSidebar: with a day agenda pending, Save/Cancel/Delete
        // return to the day's list instead of shutting the panel (DayAgendaPanel.cs).
        void IAppointmentView.ClosePanel() => CloseEditorPanel();

        /// <summary>The editor knows nothing about the calendar; the shell owns both and forwards.</summary>
        void IAppointmentView.HighlightSelection(System.DateTime? day, System.TimeSpan? timeOfDay)
            => _calendar.SetSelection(day, timeOfDay);

        /// <summary>
        /// Typing in either start box moves the calendar's marker. TextChanged, not LostFocus: the
        /// point is to see where you are landing WHILE you type.
        /// </summary>
        private void FieldStart_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => _appointments?.StartEdited();

        // ---- panel buttons ----

        // Cancel routes like the editor's own ClosePanel: back to a pending day agenda, else shut.
        private void SidebarClose_Click(object sender, RoutedEventArgs e) => CloseEditorPanel();

        private void AllDayToggle_Click(object sender, RoutedEventArgs e) => _appointments.ToggleAllDay();

        private void SaveAppointment_Click(object sender, RoutedEventArgs e) => _appointments.Save();

        private void DeleteAppointment_Click(object sender, RoutedEventArgs e) => _appointments.Delete();

        /// <summary>Opens the panel composing a new appointment at the given time.</summary>
        private void OpenNewAppointment(System.DateTime when) => _appointments.NewAt(when);

        /// <summary>Opens the panel on an existing appointment.</summary>
        private void OpenAppointment(Models.CalendarEvent ev) => _appointments.Load(ev);
    }
}
