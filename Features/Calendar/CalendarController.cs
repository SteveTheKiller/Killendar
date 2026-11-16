using System;
using Killendar.Models;
using Killendar.Services;
using Killendar.Views;

namespace Killendar.Features
{
    /// <summary>
    /// The calendar surface: the four views, which one is showing, and where in time it is
    /// anchored. Selecting an appointment or an empty slot is forwarded to the appointment editor.
    /// </summary>
    internal sealed class CalendarController
    {
        private readonly ICalendarHost _host;
        private readonly EventStore _store;
        private readonly AppointmentEditor _editor;

        private MonthView _month = null!;
        private WeekView _week = null!;
        private DayView _day = null!;
        private AgendaView _agenda = null!;
        private YearView _year = null!;

        private ICalendarView _active = null!;
        private string _activeName = "Month";
        private DateTime _anchor = DateTime.Today;
        private readonly ViewHistory _history = new();

        internal CalendarController(ICalendarHost host, EventStore store, AppointmentEditor editor)
        {
            _host = host;
            _store = store;
            _editor = editor;
        }

        /// <summary>Where the view is currently looking. "+ New" starts from here.</summary>
        internal DateTime Anchor => _anchor;

        private DateTime? _selectedDay;
        private TimeSpan? _selectedTime;

        /// <summary>
        /// What the appointment panel is editing, or (null, null) when it is shut. Held here rather
        /// than in a view so it survives switching views, and pushed into whichever view is active.
        /// </summary>
        internal void SetSelection(DateTime? day, TimeSpan? timeOfDay)
        {
            _selectedDay = day?.Date;
            _selectedTime = timeOfDay;
            _active?.SetSelection(_selectedDay, _selectedTime);
        }

        /// <summary>Builds the views and shows Month. Does NOT open the Killendar - that has to
        /// wait until the window is on screen, because an encrypted one prompts for a password and
        /// a modal dialog needs a shown owner.</summary>
        internal void Initialize()
        {
            _month = new MonthView();
            _week = new WeekView();
            _day = new DayView();
            _agenda = new AgendaView();
            _year = new YearView();
            // A day in the year opens that day, not just its agenda: the year is for finding the
            // date, and the Day view is where the date's appointments are.
            _year.DayOpened += d =>
            {
                Navigate("Day", d.Date);
                _host.ShowDayAgenda(d.Date, null);
            };

            foreach (ICalendarView v in new ICalendarView[] { _month, _week, _day, _agenda, _year })
            {
                // Clicking an appointment or a day opens the day's AGENDA in the sidebar - the
                // editor is behind each row's Edit action, never the default (2026-07-30).
                // Only SlotSelected, the explicit create gesture, still goes straight to the form.
                v.EventSelected += ev => _host.ShowDayAgenda(ev.Start.Date, ev);
                v.DaySelected += d => _host.ShowDayAgenda(d, null);
                v.SlotSelected += at => _editor.NewAt(at);
                // Ctrl+wheel over a time grid. The shell owns the setting because it is persisted
                // and shared by Week and Day; Month and Agenda never raise it.
                v.DensityStepped += _host.StepDensity;

                // Drag and drop, wired by type rather than through ICalendarView so Agenda - a
                // list with nowhere to drop - does not have to carry a dead event.
                if (v is TimeGridView grid) grid.EventDropped += MoveDropped;
                if (v is MonthView month)
                {
                    month.EventDropped += MoveDropped;
                    month.ZoomChanged += () =>
                    {
                        RefreshPeriodLabel();
                        _host.ShowFineMonthNavigation(_active == _month && _month.IsRollingMode);
                    };
                }
            }

            // The chip context menu's "Edit the series": load the MASTER, which the editor
            // treats as editing the series itself - pattern controls, no scope question.
            CalendarChrome.EditSeriesRequested = ev =>
            {
                var master = _store.GetSeriesMaster(ev);
                if (master != null) _editor.Load(master);
            };
            // The appointment card's strip. Duplicate and Delete act on what the editor loaded,
            // so they run only when the load went through: a draft kept by its question stays
            // the editor's, and is never the one copied or deleted.
            CalendarChrome.EditRequested = ev => _editor.Load(ev);
            CalendarChrome.DuplicateRequested = ev => { if (_editor.Load(ev)) _editor.Duplicate(); };
            CalendarChrome.DeleteRequested = ev => { if (_editor.Load(ev)) _editor.Delete(); };

            Navigate("Month", _anchor, record: false);
        }

        internal void SelectView(string which) => Navigate(which, _anchor);

        /// <summary>Back through the views and dates visited (mouse back button, Alt+Left).</summary>
        internal void GoBack()
        {
            if (_history.Back(Here) is ViewHistory.Spot to) Navigate(to.View, to.Anchor, record: false);
        }

        /// <summary>Forward again after Back (mouse forward button, Alt+Right).</summary>
        internal void GoForward()
        {
            if (_history.Forward(Here) is ViewHistory.Spot to) Navigate(to.View, to.Anchor, record: false);
        }

        private ViewHistory.Spot Here => new(_activeName, _anchor);

        /// <summary>Shows <paramref name="which"/> at <paramref name="anchor"/>. A real change of
        /// view or date is a visit that Back can return from.</summary>
        private void Navigate(string which, DateTime anchor, bool record = true)
        {
            if (record && _active != null && (which != _activeName || anchor != _anchor))
                _history.Visit(Here);
            _anchor = anchor;
            _activeName = which switch
            {
                "Week" or "Day" or "Agenda" or "Year" => which,
                _ => "Month",
            };
            _active = which switch
            {
                "Week"   => _week,
                "Day"    => _day,
                "Agenda" => _agenda,
                "Year"   => _year,
                _        => _month
            };

            // Initialize is idempotent for our purposes: it rebinds the store and repaints.
            _active.Initialize(_store);
            _active.Anchor = _anchor;
            // After Anchor, which rebuilds: the marker is applied to freshly built cells.
            _active.SetSelection(_selectedDay, _selectedTime);

            _host.ShowView(_active);
            _host.HighlightTab(_activeName);
            _host.ShowFineMonthNavigation(_active == _month && _month.IsRollingMode);
            RefreshPeriodLabel();
        }

        /// <summary>Repaints the active view and its period label. The one call every other feature
        /// makes after changing the store underneath it.</summary>
        internal void Refresh()
        {
            _active.Refresh();
            // Refresh rebuilds the cells, so the marker has to be re-applied to the new ones.
            _active.SetSelection(_selectedDay, _selectedTime);
            RefreshPeriodLabel();
        }

        internal void RefreshPeriodLabel() => _host.PeriodLabel = _active.PeriodLabel;

        internal bool MonthRollingMode => _month.IsRollingMode;

        internal void SetMonthRollingMode(bool rolling)
        {
            _month.SetRollingMode(rolling);
            if (_active == _month)
            {
                RefreshPeriodLabel();
                _host.ShowFineMonthNavigation(_month.IsRollingMode);
            }
        }

        internal void GoTo(DateTime day) => JumpTo(day.Date);

        internal void GoToday() => JumpTo(DateTime.Today);

        private void JumpTo(DateTime day)
        {
            if (day != _anchor) _history.Visit(Here);
            _anchor = day;
            _active.Anchor = _anchor;
            RefreshPeriodLabel();
        }

        internal void Move(int direction)
        {
            _anchor = _active.Step(_anchor, direction);
            _active.Anchor = _anchor;
            RefreshPeriodLabel();
        }

        internal void MoveOneWeek(int direction)
        {
            if (_active != _month || !_month.IsRollingMode) return;
            _anchor = _anchor.AddDays(7 * direction);
            _active.Anchor = _anchor;
            RefreshPeriodLabel();
        }

        internal bool ToggleFocusedMonthAppointment()
            => _active == _month && _month.ToggleFocusedAppointmentSize();

        /// <summary>"+ New" starts on the SELECTED day when one is marked - clicking July 15 and
        /// pressing New must compose on July 15, and every day click moves the mark with it
        /// (2026-07-31). With nothing selected it starts on whatever the view is showing,
        /// not always today: composing for a week you are looking at should not silently jump
        /// back to this week.</summary>
        internal void NewAtAnchor()
        {
            var day = _selectedDay ?? _anchor.Date;
            _editor.NewAt(day.AddHours(9));
        }

        /// <summary>
        /// Commits a drag: same start-of-day slot arithmetic the views already did, duration
        /// kept. A generated series date (its Id still carries the master's) becomes a
        /// "just this date" override via SaveOccurrence - dragging never reshapes the series
        /// (2026-07-31); a plain appointment or an existing override just moves. The
        /// store raises Changed, which repaints every view.
        /// </summary>
        internal void MoveDropped(CalendarEvent ev, DateTime newStart)
        {
            var edited = ev.Clone();

            if (ev.AllDay)
            {
                // Month drags all-day chips date to date. The span rides along: the exclusive
                // end keeps the day count, so a three-day run dropped on the 10th covers the
                // 10th through the 12th.
                var span = ev.End.Date - ev.Start.Date;
                if (span < TimeSpan.FromDays(1)) span = TimeSpan.FromDays(1);
                edited.Start = newStart.Date;
                edited.End   = newStart.Date + span;
            }
            else
            {
                var dur = ev.Duration;
                if (dur <= TimeSpan.Zero) dur = TimeSpan.FromHours(1);
                edited.Start = newStart;
                edited.End   = newStart + dur;
            }

            edited.Modified = DateTime.UtcNow;

            if (ev.SeriesId is Guid sid && ev.Id == sid) _store.SaveOccurrence(edited);
            else _store.Update(edited);
            // The editor works on its own copy; tell it, or its Save puts the old times back.
            if (_store.LoadError == null) _editor.Moved(ev, edited);
        }

        /// <summary>Status text for the state the store came up in, in order of how much it tells
        /// the user. Null when there is nothing worth saying.</summary>
        internal string? OpenStatus()
        {
            if (_store.MigratedCount > 0)
                return string.Format(_host.Loc("Str_Status_Migrated"), _store.MigratedCount);
            if (_store.LoadError != null)
                return string.Format(_host.Loc("Str_Status_LoadError"), _store.LoadError);
            if (_store.MigrationWarning != null)
                return string.Format(_host.Loc("Str_Status_MigrationWarning"), _store.MigrationWarning);
            return null;
        }
    }
}
