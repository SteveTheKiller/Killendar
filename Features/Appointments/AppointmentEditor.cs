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
            _view.FieldCategories = "";

            _view.ResetRepeat();
            _view.RepeatSectionVisible = true;
            _view.SeriesScopeVisible = false;

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
            // Edit a copy: canceling must not leave half-typed changes on the stored object.
            _editing = ev.Clone();
            _allDay = ev.AllDay;

            _view.Heading = _view.Loc("Str_Side_Edit");
            _view.CanDelete = true;

            _view.FieldTitle = ev.Title;
            _view.FieldLocation = ev.Location;
            _view.FieldDescription = ev.Description;
            _view.FieldAttendees = string.Join(", ", ev.Attendees);
            _view.FieldCategories = ev.Categories;

            // Which of the three kinds of appointment this is decides what the panel offers.
            //   - a plain one: the pattern controls, no scope question;
            //   - one date OF a series: the scope chips, and the pattern belongs to the series so
            //     it is shown only once "the whole series" is chosen (LoadRepeatFrom does that);
            //   - a series master opened directly: the pattern controls, no scope question.
            _view.ResetRepeat();

            var master = ev.IsOccurrence ? _store.GetSeriesMaster(ev) : null;
            bool oneDateOfASeries = master != null;

            _view.SeriesScopeVisible = oneDateOfASeries;
            _view.EditWholeSeries = false;
            _view.RepeatSectionVisible = !oneDateOfASeries;

            LoadRepeatFrom(master ?? ev);

            _view.FieldStartDate = DateFormatManager.Format(ev.Start);
            _view.FieldStartTime = ev.Start.ToString("h:mm tt");
            // The model keeps all-day ranges half-open (the stored end is the midnight after the
            // last covered day), while the editor asks for the last day the user can see. Showing
            // the stored boundary here made every edit-save cycle extend the event by one day.
            var displayedEnd = ev.AllDay
                ? CalendarDateSpan.VisibleAllDayEnd(ev.Start, ev.End)
                : ev.End;
            _view.FieldEndDate = DateFormatManager.Format(displayedEnd);
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
            SyncHighlightedSelection();
        }

        /// <summary>
        /// Points the calendar's marker at whatever the START boxes currently say. Called when the
        /// panel opens and on every edit of either box, so correcting the date or the time moves
        /// the marker.
        ///
        /// An unparseable half-typed value clears its half rather than leaving the last value that
        /// happened to parse - a stale marker is worse than none. An all-day appointment reports no
        /// time at all, so the day stays marked and no time band is drawn.
        /// </summary>
        internal void SyncHighlightedSelection()
        {
            DateTime? day = TryParseDate(_view.FieldStartDate, out var d) ? d : (DateTime?)null;
            TimeSpan? time = null;
            if (!_allDay && TryParseTime(_view.FieldStartTime, out var t)) time = t;
            _view.HighlightSelection(day, time);
        }

        /// <summary>Either start box was edited. Wired to TextChanged by the shell.</summary>
        internal void StartEdited() => SyncHighlightedSelection();

        internal void ToggleAllDay()
        {
            _allDay = !_allDay;
            ApplyAllDay();
            // All-day has no slot, so the time band has to go (and come back on un-toggling).
            SyncHighlightedSelection();
        }

        /// <summary>All-day hides the time fields rather than disabling them: a grayed-out box you
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
                end = CalendarDateSpan.ExclusiveAllDayEnd(startDate, endDate);
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
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .ToList();

            // "Ends after N times" or "ends on a date" with nothing readable in the box would fall
            // through as "repeats forever", which is the one surprise this must never spring.
            if (_view.RepeatEndIncomplete)
            {
                Reject("Str_Err_RepeatEnd", AppointmentField.Title);
                return;
            }

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
                    Attendees   = attendees,
                    Categories  = EventStore.NormalizeCategories(_view.FieldCategories)
                };
                ApplyRepeatFromPanel(ev);
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
                _editing.Categories  = EventStore.NormalizeCategories(_view.FieldCategories);

                bool oneDateOfASeries = _store.GetSeriesMaster(_editing) != null;

                if (!oneDateOfASeries)
                {
                    // A plain appointment given a pattern BECOMES a series here, and a series
                    // whose pattern is set back to Never becomes a plain appointment again.
                    ApplyRepeatFromPanel(_editing);
                    _store.Update(_editing);
                }
                else if (_view.EditWholeSeries)
                {
                    ApplyRepeatFromPanel(_editing);
                    _store.UpdateSeries(_editing);
                }
                else
                {
                    _store.SaveOccurrence(_editing);
                }

                _view.SetStatus(string.Format(_view.Loc("Str_Status_Saved"), _editing.Title));
            }

            _view.ClosePanel();
        }

        internal void Delete()
        {
            if (_editing == null) return;

            var master = _store.GetSeriesMaster(_editing);
            bool oneDateOfASeries = master != null;

            // The scope chips are already on screen and already say which it is, so the confirm
            // only has to state plainly what is about to go - it does not ask the question again.
            string detail = _view.Loc("Str_Dlg_DeleteDetail");
            if (oneDateOfASeries)
                detail = _view.Loc(_view.EditWholeSeries
                    ? "Str_Dlg_DeleteSeriesDetail"
                    : "Str_Dlg_DeleteOneDateDetail");

            // Themed confirm, not a stock MessageBox.
            var dlg = new ConfirmDialog(
                string.Format(_view.Loc("Str_Dlg_DeleteMsg"), _editing.Title),
                detail,
                _view.Loc("Str_Btn_Delete")) { Owner = _view.Window };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            var title = _editing.Title;

            if (oneDateOfASeries && _view.EditWholeSeries) _store.DeleteSeries(master!.Id);
            else if (oneDateOfASeries)                     _store.DeleteOccurrence(_editing);
            else if (_editing.IsSeries)                    _store.DeleteSeries(_editing.Id);
            else                                           _store.Delete(_editing.Id);

            _view.SetStatus(string.Format(_view.Loc("Str_Status_Deleted"), title));
            _view.ClosePanel();
        }

        /// <summary>Copies the panel's repeat settings onto an appointment.</summary>
        private void ApplyRepeatFromPanel(CalendarEvent ev)
        {
            ev.Repeat         = _view.FieldRepeat;
            ev.RepeatInterval = _view.FieldRepeatEveryN;
            ev.RepeatDays     = _view.FieldRepeat == RepeatFreq.Weekly
                                    ? _view.FieldRepeatDays
                                    : [];
            ev.RepeatCount    = _view.FieldRepeatCount;
            ev.RepeatUntil    = _view.FieldRepeatUntil;

            // No pattern means no leftovers. A series turned back into a plain appointment must
            // not keep the dates it used to skip, or turning the repeat back on later resurrects
            // deletions the user made in a schedule that no longer exists.
            if (ev.Repeat == RepeatFreq.None)
            {
                ev.RepeatInterval = 1;
                ev.RepeatCount    = 0;
                ev.RepeatUntil    = null;
                ev.SkipDates      = [];
            }
        }

        /// <summary>Fills the panel's repeat controls from an appointment (the series master when
        /// one date of a series is being edited).</summary>
        private void LoadRepeatFrom(CalendarEvent ev)
        {
            _view.FieldRepeat      = ev.Repeat;
            _view.FieldRepeatEveryN = ev.RepeatInterval > 0 ? ev.RepeatInterval : 1;
            _view.FieldRepeatDays  = [.. ev.RepeatDays ?? []];
            if (ev.RepeatCount > 0) _view.FieldRepeatCount = ev.RepeatCount;
            else if (ev.RepeatUntil.HasValue) _view.FieldRepeatUntil = ev.RepeatUntil;
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
            [
                "h:mm tt", "hh:mm tt", "h:mmtt", "hh:mmtt", "h tt", "htt",
                "H:mm", "HH:mm", "Hmm", "HHmm", "H", "HH"
            ];
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
