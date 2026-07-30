using System;
using System.Globalization;
using System.Linq;
using Killendar.Controls;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Features
{
    /// <summary>
    /// Creating and editing an appointment. There is deliberately no dialog window: the panel is
    /// the editor.
    ///
    /// Dates and times are typed, not picked. A styled DatePicker is a lot of template for
    /// something a keyboard user types faster anyway, and the stock one ignores the theme. The
    /// pattern comes from DateFormatManager so the hint, the display and the parse can never
    /// disagree.
    /// </summary>
    internal sealed class AppointmentEditor
    {
        private readonly IAppointmentView _view;
        private readonly EventStore _store;

        /// <summary>The appointment being edited, or null when composing a new one.</summary>
        private CalendarEvent? _editing;

        private bool _allDay;

        internal AppointmentEditor(IAppointmentView view, EventStore store)
        {
            _view = view;
            _store = store;
        }

        /// <summary>True while an existing appointment is loaded, so the shell knows a delete is
        /// meaningful.</summary>
        internal bool IsEditingExisting => _editing != null;

        /// <summary>Forgets the loaded appointment. Called when the panel closes.</summary>
        internal void Clear() => _editing = null;

        internal void NewAt(DateTime when)
        {
            _editing = null;
            _allDay = false;

            _view.Heading = _view.Loc("Str_Side_New");
            _view.CanDelete = false;

            _view.FieldTitle = "";
            _view.FieldLocation = "";
            _view.FieldDescription = "";
            _view.FieldAttendees = "";

            var end = when.AddHours(1);
            _view.FieldStartDate = DateFormatManager.Format(when);
            _view.FieldStartTime = when.ToString("h:mm tt");
            _view.FieldEndDate = DateFormatManager.Format(end);
            _view.FieldEndTime = end.ToString("h:mm tt");

            Show();
            _view.Focus(AppointmentField.Title);
        }

        internal void Load(CalendarEvent ev)
        {
            // Edit a copy: cancelling must not leave half-typed changes on the stored object.
            _editing = ev.Clone();
            _allDay = ev.AllDay;

            _view.Heading = _view.Loc("Str_Side_Edit");
            _view.CanDelete = true;

            _view.FieldTitle = ev.Title;
            _view.FieldLocation = ev.Location;
            _view.FieldDescription = ev.Description;
            _view.FieldAttendees = string.Join(", ", ev.Attendees);

            _view.FieldStartDate = DateFormatManager.Format(ev.Start);
            _view.FieldStartTime = ev.Start.ToString("h:mm tt");
            _view.FieldEndDate = DateFormatManager.Format(ev.End);
            _view.FieldEndTime = ev.End.ToString("h:mm tt");

            Show();
            _view.Focus(AppointmentField.Title, selectAll: true);
        }

        private void Show()
        {
            _view.ClearError();
            _view.DateHint = DateFormatManager.Hint;
            ApplyAllDay();
            _view.OpenPanel();
        }

        internal void ToggleAllDay()
        {
            _allDay = !_allDay;
            ApplyAllDay();
        }

        /// <summary>All-day hides the time fields rather than disabling them: a greyed-out box you
        /// cannot use is just clutter once the dates carry the whole meaning.</summary>
        private void ApplyAllDay()
            => _view.SetAllDay(_view.Loc(_allDay ? "Str_Chk_AllDayOn" : "Str_Chk_AllDayOff"),
                               timesVisible: !_allDay);

        /// <summary>After a date-format change: re-render the dates already typed so the panel does
        /// not show a mix of the old pattern and the new one, and update the hint with them.</summary>
        internal void ReformatDates()
        {
            if (TryParseDate(_view.FieldStartDate, out var s)) _view.FieldStartDate = DateFormatManager.Format(s);
            if (TryParseDate(_view.FieldEndDate, out var e)) _view.FieldEndDate = DateFormatManager.Format(e);
            _view.DateHint = DateFormatManager.Hint;
        }

        /// <summary>After a language change: re-render the panel's own strings.</summary>
        internal void RefreshLocalizedText()
        {
            _view.Heading = _view.Loc(_editing == null ? "Str_Side_New" : "Str_Side_Edit");
            ApplyAllDay();
        }

        /// <summary>
        /// Validates what is on the panel and writes it to the store. Leaves the panel open with an
        /// error and the caret in the offending field when anything does not parse.
        /// </summary>
        internal void Save()
        {
            _view.ClearError();

            var title = _view.FieldTitle.Trim();
            if (title.Length == 0)
            {
                Reject("Str_Err_NoTitle", AppointmentField.Title);
                return;
            }

            if (!TryParseDate(_view.FieldStartDate, out var startDate))
            {
                RejectWithHint("Str_Err_StartDate", AppointmentField.StartDate);
                return;
            }
            if (!TryParseDate(_view.FieldEndDate, out var endDate))
            {
                RejectWithHint("Str_Err_EndDate", AppointmentField.EndDate);
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
                if (!TryParseTime(_view.FieldStartTime, out var startTime))
                {
                    Reject("Str_Err_StartTime", AppointmentField.StartTime);
                    return;
                }
                if (!TryParseTime(_view.FieldEndTime, out var endTime))
                {
                    Reject("Str_Err_EndTime", AppointmentField.EndTime);
                    return;
                }
                start = startDate.Date + startTime;
                end = endDate.Date + endTime;

                if (end <= start)
                {
                    Reject("Str_Err_EndBeforeStart", AppointmentField.EndTime);
                    return;
                }
            }

            var attendees = _view.FieldAttendees
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
                    Location    = _view.FieldLocation.Trim(),
                    Description = _view.FieldDescription.Trim(),
                    Attendees   = attendees
                };
                _store.Add(ev);
                _view.SetStatus(string.Format(_view.Loc("Str_Status_Added"), ev.Title));
            }
            else
            {
                _editing.Title       = title;
                _editing.Start       = start;
                _editing.End         = end;
                _editing.AllDay      = _allDay;
                _editing.Location    = _view.FieldLocation.Trim();
                _editing.Description = _view.FieldDescription.Trim();
                _editing.Attendees   = attendees;
                _store.Update(_editing);
                _view.SetStatus(string.Format(_view.Loc("Str_Status_Saved"), _editing.Title));
            }

            _view.ClosePanel();
        }

        internal void Delete()
        {
            if (_editing == null) return;

            // Themed confirm, not a stock MessageBox.
            var dlg = new ConfirmDialog(
                string.Format(_view.Loc("Str_Dlg_DeleteMsg"), _editing.Title),
                _view.Loc("Str_Dlg_DeleteDetail"),
                _view.Loc("Str_Btn_Delete")) { Owner = _view.Window };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            var title = _editing.Title;
            _store.Delete(_editing.Id);
            _view.SetStatus(string.Format(_view.Loc("Str_Status_Deleted"), title));
            _view.ClosePanel();
        }

        private void Reject(string key, AppointmentField field)
        {
            _view.ShowError(_view.Loc(key));
            _view.Focus(field);
        }

        private void RejectWithHint(string key, AppointmentField field)
        {
            _view.ShowError(_view.Loc(key) + " (" + DateFormatManager.Hint + ")");
            _view.Focus(field);
        }

        // ---- parsing ----

        /// <summary>
        /// Accepts what people actually type: "9", "9:30", "9:30 pm", "21:30", "0930".
        /// False only when nothing sensible can be read out of it.
        /// </summary>
        internal static bool TryParseTime(string raw, out TimeSpan time)
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

        internal static bool TryParseDate(string raw, out DateTime date)
            => DateFormatManager.TryParse(raw, out date);
    }
}
