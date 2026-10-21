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

        /// <summary>The start the end boxes were last aligned to. When the start moves, the end
        /// moves by the same amount so the appointment keeps its length (#14). Null until the
        /// panel has finished loading, so filling the boxes never counts as an edit.</summary>
        private DateTime? _followedStart;
        private bool _loading;
        private CultureInfo _timeCulture = CultureInfo.CurrentCulture;
        private DateTime? _nominalEnd;

        internal AppointmentEditor(IAppointmentView view, EventStore store)
        {
            _view = view;
            _store = store;
        }

        /// <summary>True while an existing appointment is loaded, so the shell knows a delete is
        /// meaningful.</summary>
        internal bool IsEditingExisting => _editing != null;

        /// <summary>Forgets the loaded appointment. Called when the panel closes, which is how
        /// Cancel, Esc and the panel chevron discard on purpose.</summary>
        internal void Clear() { _editing = null; _nominalEnd = null; _baseline = null; }

        /// <summary>The panel's contents as they were right after it opened. Null while the panel
        /// is not composing or editing anything.</summary>
        private string? _baseline;

        /// <summary>True when the panel holds input that differs from what it opened with.</summary>
        internal bool HasUnsavedChanges => _baseline != null && Snapshot() != _baseline;

        /// <summary>
        /// Called before anything replaces the panel's contents: a new appointment, another one,
        /// a day's agenda, another Killendar, or quitting. True when there is nothing to lose or the
        /// user chose to discard it; false means leave everything as it is.
        /// </summary>
        internal bool ConfirmReplace()
        {
            if (!HasUnsavedChanges) return true;
            if (!_view.ConfirmDiscardDraft()) return false;
            Clear();
            return true;
        }

        /// <summary>Every value the panel would save, as one comparable string.</summary>
        private string Snapshot() => string.Join("\u0001",
            _view.FieldTitle, _view.FieldLocation, _view.FieldDescription, _view.FieldAttendees,
            _view.FieldCategories, _view.FieldStartDate, _view.FieldStartTime, _view.FieldEndDate,
            _view.FieldEndTime, _allDay, _view.FieldRepeat, _view.FieldRepeatEveryN,
            string.Join(",", _view.FieldRepeatDays ?? []), _view.FieldRepeatCount,
            _view.FieldRepeatUntil?.Ticks, _view.EditWholeSeries);

        internal void NewAt(DateTime when)
        {
            if (!ConfirmReplace()) return;
            if (when.Ticks > DateTime.MaxValue.Ticks - TimeSpan.TicksPerHour)
            {
                var message = _view.Loc("Str_Err_DateRange");
                _view.ShowError(message);
                _view.SetStatus(message);
                return;
            }
            _loading = true;
            _timeCulture = CultureInfo.CurrentCulture;
            _editing = null;
            _nominalEnd = null;
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
            _view.FieldStartTime = FormatTime(when);
            _view.FieldEndDate = DateFormatManager.Format(end);
            _view.FieldEndTime = FormatTime(end);

            Show();
            _view.Focus(AppointmentField.Title);
        }

        internal void Load(CalendarEvent ev)
        {
            if (!ConfirmReplace()) return;
            // Edit a copy: canceling must not leave half-typed changes on the stored object.
            _loading = true;
            _timeCulture = CultureInfo.CurrentCulture;
            _editing = ev.Clone();
            _nominalEnd = !ev.AllDay && NominalDuration.HasValue(ev) ? ev.End : (DateTime?)null;
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
            _view.FieldStartTime = FormatTime(ev.Start);
            // The model keeps all-day ranges half-open (the stored end is the midnight after the
            // last covered day), while the editor asks for the last day the user can see. Showing
            // the stored boundary here made every edit-save cycle extend the event by one day.
            var displayedEnd = ev.AllDay
                ? CalendarDateSpan.VisibleAllDayEnd(ev.Start, ev.End)
                : ev.End;
            _view.FieldEndDate = DateFormatManager.Format(displayedEnd);
            _view.FieldEndTime = FormatTime(ev.End);

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
            _loading = false;
            _followedStart = CurrentStart();
            _baseline = Snapshot();
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
        internal void StartEdited()
        {
            SyncHighlightedSelection();
            FollowStart();
        }

        /// <summary>The instant the start boxes describe, or null while one of them is unreadable
        /// (mid-edit, or blank). All-day reads the date alone.</summary>
        private DateTime? CurrentStart()
        {
            if (!TryParseDate(_view.FieldStartDate, out var d)) return null;
            if (_allDay) return d.Date;
            return TryParseTime(_view.FieldStartTime, out var t) ? d.Date + t : (DateTime?)null;
        }

        /// <summary>
        /// Moves the end by however far the start just moved, so a 9 to 10 that is retyped as 8
        /// becomes 8 to 9 rather than 8 to 10 (#14). Runs on every keystroke, so it works from the
        /// last start that was readable: "8", then "8:3", then "8:30" apply three small shifts that
        /// add up to the one the user meant. Nothing happens while the start is half-typed, and an
        /// unreadable end is left alone rather than guessed at.
        /// </summary>
        private void FollowStart()
        {
            if (_loading) return;
            var now = CurrentStart();
            if (now == null) return;
            var was = _followedStart;
            if (was == null || was == now) { _followedStart = now; return; }
            bool readableEnd = TryParseDate(_view.FieldEndDate, out _) &&
                               (_allDay || TryParseTime(_view.FieldEndTime, out _));
            if (_nominalEnd.HasValue && _editing != null && !_allDay)
            {
                if (NominalDuration.HasValue(_editing) &&
                    TryParseDate(_view.FieldEndDate, out var nominalEndDate) &&
                    TryParseTime(_view.FieldEndTime, out var nominalEndTime) &&
                    MatchesDisplayedInstant(nominalEndDate.Date + nominalEndTime, _nominalEnd.Value))
                {
                    DateTime movedEnd;
                    try
                    {
                        movedEnd = NominalDuration.EndAfterStartChange(_editing,
                            PreserveDisplayedInstant(now.Value, _editing.Start));
                    }
                    catch (ArgumentOutOfRangeException) { return; }
                    catch (OverflowException) { return; }
                    _followedStart = now;
                    _nominalEnd = movedEnd;
                    _view.FieldEndDate = DateFormatManager.Format(movedEnd);
                    _view.FieldEndTime = FormatTime(movedEnd);
                    return;
                }
                _nominalEnd = null;
            }
            if (!TryFollowStart(was.Value, now.Value, _allDay, _view.FieldEndDate, _view.FieldEndTime,
                                out var endDate, out var endTime))
            {
                if (!readableEnd) _followedStart = now;
                return;
            }
            _followedStart = now;
            _view.FieldEndDate = endDate;
            _view.FieldEndTime = endTime;
        }

        /// <summary>The arithmetic behind FollowStart, kept free of the view so it can be tested.
        /// False when the end boxes cannot be read, in which case they are left as they are.</summary>
        internal static bool TryFollowStart(DateTime was, DateTime now, bool allDay,
                                            string endDateText, string endTimeText,
                                            out string endDate, out string endTime)
        {
            endDate = endDateText;
            endTime = endTimeText;
            if (!TryParseDate(endDateText, out var d)) return false;

            if (allDay)
            {
                // The time boxes are hidden; only whole days move, and the time text is kept for
                // when all-day is toggled back off.
                long ticks = d.Date.Ticks + (now.Date.Ticks - was.Date.Ticks);
                if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
                endDate = DateFormatManager.Format(new DateTime(ticks));
                return true;
            }

            if (!TryParseTime(endTimeText, out var t)) return false;
            long movedTicks = d.Date.Ticks + t.Ticks + (now.Ticks - was.Ticks);
            if (movedTicks < DateTime.MinValue.Ticks || movedTicks > DateTime.MaxValue.Ticks) return false;
            var moved = new DateTime(movedTicks);
            endDate = DateFormatManager.Format(moved);
            endTime = FormatTime(moved);
            return true;
        }

        internal void ToggleAllDay()
        {
            _nominalEnd = null;
            _allDay = !_allDay;
            ApplyAllDay();
            // All-day has no slot, so the time band has to go (and come back on un-toggling).
            SyncHighlightedSelection();
            // The start now means something different (a day, or a day and a time), so the next
            // edit must measure from the new meaning, not shift the end by a whole time of day.
            _followedStart = CurrentStart();
        }

        internal static CalendarEvent[] RepeatPreview(CalendarEvent probe)
        {
            probe = probe.Clone();
            long available = DateTime.MaxValue.Ticks - probe.Start.Ticks;
            if (available == 0) return [];
            probe.End = probe.Start.AddTicks(Math.Min(TimeSpan.TicksPerHour, available));
            var limit = probe.Start.Year > 9996 ? DateTime.MaxValue : probe.Start.AddYears(3);
            return Recurrence.Expand(probe, probe.Start, limit).Take(4).ToArray();
        }

        /// <summary>All-day hides the time fields rather than disabling them: a grayed-out box you
        /// cannot use is just clutter once the dates carry the whole meaning.</summary>
        private void ApplyAllDay()
            => _view.SetAllDay(_view.Loc(_allDay ? "Str_Chk_AllDayOn" : "Str_Chk_AllDayOff"),
                               timesVisible: !_allDay);

        /// <summary>The clock changed: show the time boxes in it. Like a date style change, this
        /// is not an edit, so a panel with nothing unsaved stays that way.</summary>
        internal void ChangeTimeStyle(TimeStyle style)
        {
            bool clean = !HasUnsavedChanges;
            bool haveStart = TryParseTime(_view.FieldStartTime, out var start);
            bool haveEnd = TryParseTime(_view.FieldEndTime, out var end);
            bool loading = _loading;
            _loading = true;
            try
            {
                DateFormatManager.ApplyTime(style);
                if (haveStart) _view.FieldStartTime = FormatTime(DateTime.Today + start);
                if (haveEnd) _view.FieldEndTime = FormatTime(DateTime.Today + end);
            }
            finally { _loading = loading; }
            _followedStart = CurrentStart();
            Rebaseline(clean);
        }

        /// <summary>Reformatting rewrites the field text; that only counts as an edit if there was
        /// one already.</summary>
        private void Rebaseline(bool clean)
        {
            if (clean && _baseline != null) _baseline = Snapshot();
        }

        /// <summary>After a date-format change: re-render the dates already typed so the panel does
        /// not show a mix of the old pattern and the new one, and update the hint with them.</summary>
        internal void ChangeDateStyle(DateStyle style)
        {
            bool clean = !HasUnsavedChanges;
            bool haveStart = TryParseDate(_view.FieldStartDate, out var start);
            bool haveEnd = TryParseDate(_view.FieldEndDate, out var end);
            var until = _view.FieldRepeatUntil;
            bool loading = _loading;
            _loading = true;
            try
            {
                DateFormatManager.Apply(style);
                if (haveStart) _view.FieldStartDate = DateFormatManager.Format(start);
                if (haveEnd) _view.FieldEndDate = DateFormatManager.Format(end);
                if (until.HasValue) _view.FieldRepeatUntil = until;
                _view.DateHint = DateFormatManager.Hint;
            }
            finally { _loading = loading; }
            _followedStart = CurrentStart();
            Rebaseline(clean);
        }

        /// <summary>After a language change: re-render the panel's own strings.</summary>
        internal void RefreshLocalizedText()
        {
            bool clean = !HasUnsavedChanges;
            // The locale has already changed, but the open fields still use the previous
            // culture's date pattern and separators. Capture them before reformatting.
            var culture = CultureInfo.CurrentCulture;
            bool haveStartDate, haveEndDate;
            DateTime startDate, endDate;
            DateTime? until;
            try
            {
                CultureInfo.CurrentCulture = _timeCulture;
                haveStartDate = TryParseDate(_view.FieldStartDate, out startDate);
                haveEndDate = TryParseDate(_view.FieldEndDate, out endDate);
                until = _view.FieldRepeatUntil;
            }
            finally { CultureInfo.CurrentCulture = culture; }
            bool loading = _loading;
            _loading = true;
            try
            {
                if (haveStartDate) _view.FieldStartDate = DateFormatManager.Format(startDate);
                if (haveEndDate) _view.FieldEndDate = DateFormatManager.Format(endDate);
                if (until.HasValue) _view.FieldRepeatUntil = until;
                _view.DateHint = DateFormatManager.Hint;
                if (TryParseTime(_view.FieldStartTime, _timeCulture, out var start))
                    _view.FieldStartTime = FormatTime(DateTime.Today + start);
                if (TryParseTime(_view.FieldEndTime, _timeCulture, out var end))
                    _view.FieldEndTime = FormatTime(DateTime.Today + end);
                _timeCulture = CultureInfo.CurrentCulture;
            }
            finally { _loading = loading; }
            _followedStart = CurrentStart();
            _view.Heading = _view.Loc(_editing == null ? "Str_Side_New" : "Str_Side_Edit");
            ApplyAllDay();
            Rebaseline(clean);
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
            bool preserveNominalDuration = false;
            if (_allDay)
            {
                start = startDate.Date;
                if (endDate.Date == DateTime.MaxValue.Date || startDate.Date == DateTime.MaxValue.Date)
                {
                    Reject("Str_Err_DateRange", AppointmentField.EndDate);
                    return;
                }
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
                preserveNominalDuration = _editing != null && !_editing.AllDay &&
                    _nominalEnd.HasValue && MatchesDisplayedInstant(end, _nominalEnd.Value);
                if (_editing != null && !_editing.AllDay)
                {
                    start = PreserveDisplayedInstant(start, _editing.Start);
                    end = preserveNominalDuration ? _nominalEnd!.Value : PreserveDisplayedInstant(end, _editing.End);
                }

                if (end.ToUniversalTime() <= start.ToUniversalTime())
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
            bool appliesPattern = _editing == null || !_editing.IsOccurrence || _view.EditWholeSeries;
            if (appliesPattern && _view.FieldRepeat != RepeatFreq.None && _view.FieldRepeatEveryN < 1)
            {
                Reject("Str_Err_RepeatInterval", AppointmentField.RepeatInterval);
                return;
            }
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
                if (_store.LoadError != null) { _view.ShowError(_store.LoadError); return; }
                _view.SetStatus(string.Format(_view.Loc("Str_Status_Added"), ev.Title));
            }
            else
            {
                string previousDescription = _editing.Description;
                bool previousAllDay = _editing.AllDay;
                _editing.Title       = title;
                _editing.Start       = start;
                _editing.End         = end;
                _editing.AllDay      = _allDay;
                if (!preserveNominalDuration)
                {
                    _editing.DurationDays = 0;
                    _editing.DurationTimeTicks = 0;
                }
                _editing.Location    = _view.FieldLocation.Trim();
                _editing.Description = _view.FieldDescription.Trim();
                _editing.Attendees   = attendees;
                _editing.Categories  = EventStore.NormalizeCategories(_view.FieldCategories);
                IcsService.DropStaleKeptProperties(_editing, previousDescription, previousAllDay);

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

                if (_store.LoadError != null) { _view.ShowError(_store.LoadError); return; }
                _view.SetStatus(string.Format(_view.Loc("Str_Status_Saved"), _editing.Title));
            }

            _baseline = null;   // saved: nothing left to lose
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

            if (_store.LoadError != null) { _view.ShowError(_store.LoadError); return; }
            _view.SetStatus(string.Format(_view.Loc("Str_Status_Deleted"), title));
            _baseline = null;
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

        private static DateTime PreserveDisplayedInstant(DateTime entered, DateTime original)
        {
            // The fields display minutes, so an unchanged value must retain hidden seconds
            // and the original daylight-saving occurrence of a repeated local hour.
            return MatchesDisplayedInstant(entered, original) ? original : entered;
        }

        private static bool MatchesDisplayedInstant(DateTime entered, DateTime original)
            => entered.Ticks == original.Ticks - original.Ticks % TimeSpan.TicksPerMinute;

        /// <summary>The time boxes use the display clock in one of the two forms TryParseTime reads
        /// back exactly, whatever the culture's own short time looks like.</summary>
        private static string FormatTime(DateTime value)
        {
            var culture = CultureInfo.CurrentCulture;
            return DateFormatManager.Uses12Hour(culture)
                ? value.ToString("h:mm tt", DateFormatManager.ClockFormat(culture))
                : value.ToString("HH:mm", culture);
        }

        /// <summary>
        /// Accepts what people actually type: "9", "9:30", "9:30 pm", "21:30", "0930".
        /// False only when nothing sensible can be read out of it.
        /// </summary>
        internal static bool TryParseTime(string raw, out TimeSpan time)
            => TryParseTime(raw, CultureInfo.CurrentCulture, out time);

        private static bool TryParseTime(string raw, CultureInfo culture, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return false;

            // "%H", not "H": a one-character format string is read as a STANDARD specifier, and
            // there is no standard "H", so the framework throws FormatException instead of
            // returning false. It was only reached once every earlier pattern had failed, which
            // is exactly what a bare "8" or a half-typed "8A" does, so typing an hour crashed the
            // app on the first keystroke (#14). The percent sign says "custom pattern".
            //
            // 24-hour patterns first. In cultures with no AM/PM designators (German among them)
            // an empty "tt" matches nothing and defaults to PM, so with the 12-hour patterns
            // ahead a bare "8" came back as 20:00. Nothing with a real "am"/"pm" on it can
            // match a 24-hour pattern, so the order costs those inputs nothing.
            string[] formats =
            [
                "H:mm", "HH:mm", "Hmm", "HHmm", "%H", "HH",
                "h:mm tt", "hh:mm tt", "h:mmtt", "hh:mmtt", "h tt", "htt"
            ];
            // The clock format adds AM/PM where the language has none, so a 12-hour time the
            // editor wrote in such a language reads back.
            if (DateTime.TryParseExact(raw, formats, culture,
                                       DateTimeStyles.None, out var dt) ||
                DateTime.TryParseExact(raw, formats, DateFormatManager.ClockFormat(culture),
                                       DateTimeStyles.None, out dt) ||
                DateTime.TryParse(raw, culture, DateTimeStyles.NoCurrentDateDefault, out dt))
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
