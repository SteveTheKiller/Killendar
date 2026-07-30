using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using Killendar.Models;

// The Killendar - appointment sidebar. Partial of MainWindow.
// Creating and editing both happen here; there is deliberately no dialog window.
namespace Killendar
{
    public partial class MainWindow
    {
        /// <summary>The appointment being edited, or null when the sidebar is composing a new one.</summary>
        private CalendarEvent? _editing;

        private bool _allDay;

        // Dates and times are typed, not picked. A styled DatePicker is a lot of template for
        // something a keyboard user types faster anyway, and the stock one ignores the theme.
        // The pattern itself comes from DateFormatManager so the hint, the display and the parse
        // can never disagree.

        private void OpenNewAppointment(DateTime when)
        {
            _editing = null;
            _allDay = false;

            SidebarTitle.Text = Loc("Str_Side_New");
            DeleteAppointmentBtn.Visibility = Visibility.Collapsed;

            FieldTitle.Text = "";
            FieldLocation.Text = "";
            FieldDescription.Text = "";
            FieldAttendees.Text = "";

            var start = when;
            var end = when.AddHours(1);
            FieldStartDate.Text = Services.DateFormatManager.Format(start);
            FieldStartTime.Text = start.ToString("h:mm tt");
            FieldEndDate.Text = Services.DateFormatManager.Format(end);
            FieldEndTime.Text = end.ToString("h:mm tt");

            ShowSidebar();
            FieldTitle.Focus();
        }

        private void OpenAppointment(CalendarEvent ev)
        {
            // Edit a copy: cancelling must not leave half-typed changes on the stored object.
            _editing = ev.Clone();
            _allDay = ev.AllDay;

            SidebarTitle.Text = Loc("Str_Side_Edit");
            DeleteAppointmentBtn.Visibility = Visibility.Visible;

            FieldTitle.Text = ev.Title;
            FieldLocation.Text = ev.Location;
            FieldDescription.Text = ev.Description;
            FieldAttendees.Text = string.Join(", ", ev.Attendees);

            FieldStartDate.Text = Services.DateFormatManager.Format(ev.Start);
            FieldStartTime.Text = ev.Start.ToString("h:mm tt");
            FieldEndDate.Text = Services.DateFormatManager.Format(ev.End);
            FieldEndTime.Text = ev.End.ToString("h:mm tt");

            ShowSidebar();
            FieldTitle.Focus();
            FieldTitle.SelectAll();
        }

        /// <summary>Date hints show the pattern actually in force, so they cannot promise a
        /// format the parser would reject.</summary>
        private void RefreshDateHints()
        {
            var hint = Services.DateFormatManager.Hint;
            FieldStartDate.ToolTip = hint;
            FieldEndDate.ToolTip = hint;
        }

        private void ShowSidebar()
        {
            ClearSidebarError();
            RefreshDateHints();
            ApplyAllDayState();
            OpenSidebarPanel();   // Sidebar.cs - slides the column open
        }

        private void SidebarClose_Click(object sender, RoutedEventArgs e) => CloseSidebar();

        private void AllDayToggle_Click(object sender, RoutedEventArgs e)
        {
            _allDay = !_allDay;
            ApplyAllDayState();
        }

        /// <summary>All-day hides the time fields rather than disabling them: a greyed-out box you
        /// cannot use is just clutter once the dates carry the whole meaning.</summary>
        private void ApplyAllDayState()
        {
            AllDayToggle.Content = Loc(_allDay ? "Str_Chk_AllDayOn" : "Str_Chk_AllDayOff");
            var vis = _allDay ? Visibility.Hidden : Visibility.Visible;
            FieldStartTime.Visibility = vis;
            FieldEndTime.Visibility = vis;
        }

        private void ShowSidebarError(string message)
        {
            SidebarError.Text = message;
            SidebarError.Visibility = Visibility.Visible;
        }

        private void ClearSidebarError()
        {
            SidebarError.Text = "";
            SidebarError.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Accepts what people actually type: "9", "9:30", "9:30 pm", "21:30", "0930".
        /// Returns false only when nothing sensible can be read out of it.
        /// </summary>
        private static bool TryParseTime(string raw, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return false;

            string[] formats =
            {
                "h:mm tt", "hh:mm tt", "h:mmtt", "hh:mmtt", "h tt", "htt",
                "H:mm", "HH:mm", "Hmm", "HHmm", "H", "HH"
            };
            if (DateTime.TryParseExact(raw, formats, CultureInfo.CurrentCulture,
                                       DateTimeStyles.None, out var dt) ||
                DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out dt))
            {
                time = dt.TimeOfDay;
                return true;
            }
            return false;
        }

        private static bool TryParseDate(string raw, out DateTime date)
            => Services.DateFormatManager.TryParse(raw, out date);

        private void SaveAppointment_Click(object sender, RoutedEventArgs e)
        {
            ClearSidebarError();

            var title = FieldTitle.Text.Trim();
            if (title.Length == 0)
            {
                ShowSidebarError(Loc("Str_Err_NoTitle"));
                FieldTitle.Focus();
                return;
            }

            if (!TryParseDate(FieldStartDate.Text, out var startDate))
            {
                ShowSidebarError(Loc("Str_Err_StartDate") + " (" + Services.DateFormatManager.Hint + ")");
                FieldStartDate.Focus();
                return;
            }
            if (!TryParseDate(FieldEndDate.Text, out var endDate))
            {
                ShowSidebarError(Loc("Str_Err_EndDate") + " (" + Services.DateFormatManager.Hint + ")");
                FieldEndDate.Focus();
                return;
            }

            DateTime start, end;
            if (_allDay)
            {
                start = startDate.Date;
                // All-day is stored half-open, so a one-day event ends at the next midnight.
                end = endDate.Date <= startDate.Date ? startDate.Date.AddDays(1) : endDate.Date.AddDays(1);
            }
            else
            {
                if (!TryParseTime(FieldStartTime.Text, out var startTime))
                {
                    ShowSidebarError(Loc("Str_Err_StartTime"));
                    FieldStartTime.Focus();
                    return;
                }
                if (!TryParseTime(FieldEndTime.Text, out var endTime))
                {
                    ShowSidebarError(Loc("Str_Err_EndTime"));
                    FieldEndTime.Focus();
                    return;
                }
                start = startDate.Date + startTime;
                end = endDate.Date + endTime;

                if (end <= start)
                {
                    ShowSidebarError(Loc("Str_Err_EndBeforeStart"));
                    FieldEndTime.Focus();
                    return;
                }
            }

            var attendees = FieldAttendees.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .ToList();

            if (_editing == null)
            {
                var ev = new CalendarEvent
                {
                    Title       = title,
                    Start       = start,
                    End         = end,
                    AllDay      = _allDay,
                    Location    = FieldLocation.Text.Trim(),
                    Description = FieldDescription.Text.Trim(),
                    Attendees   = attendees
                };
                _store.Add(ev);
                StatusText.Text = string.Format(Loc("Str_Status_Added"), ev.Title);
            }
            else
            {
                _editing.Title       = title;
                _editing.Start       = start;
                _editing.End         = end;
                _editing.AllDay      = _allDay;
                _editing.Location    = FieldLocation.Text.Trim();
                _editing.Description = FieldDescription.Text.Trim();
                _editing.Attendees   = attendees;
                _store.Update(_editing);
                StatusText.Text = string.Format(Loc("Str_Status_Saved"), _editing.Title);
            }

            CloseSidebar();
        }

        private void DeleteAppointment_Click(object sender, RoutedEventArgs e)
        {
            if (_editing == null) return;

            // Themed confirm, not a stock MessageBox - the family swapped those out everywhere.
            var dlg = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_DeleteMsg"), _editing.Title),
                Loc("Str_Dlg_DeleteDetail"),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            var title = _editing.Title;
            _store.Delete(_editing.Id);
            StatusText.Text = string.Format(Loc("Str_Status_Deleted"), title);
            CloseSidebar();
        }
    }
}
