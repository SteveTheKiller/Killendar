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

        private ICalendarView _active = null!;
        private DateTime _anchor = DateTime.Today;

        internal CalendarController(ICalendarHost host, EventStore store, AppointmentEditor editor)
        {
            _host = host;
            _store = store;
            _editor = editor;
        }

        /// <summary>Where the view is currently looking. "+ New" starts from here.</summary>
        internal DateTime Anchor => _anchor;

        /// <summary>Builds the views and shows Month. Does NOT open the Killendar - that has to
        /// wait until the window is on screen, because an encrypted one prompts for a password and
        /// a modal dialog needs a shown owner.</summary>
        internal void Initialize()
        {
            _month = new MonthView();
            _week = new WeekView();
            _day = new DayView();
            _agenda = new AgendaView();

            foreach (ICalendarView v in new ICalendarView[] { _month, _week, _day, _agenda })
            {
                v.EventSelected += _editor.Load;
                v.SlotSelected += _editor.NewAt;
            }

            SelectView("Month");
        }

        internal void SelectView(string which)
        {
            _active = which switch
            {
                "Week"   => _week,
                "Day"    => _day,
                "Agenda" => _agenda,
                _        => _month
            };

            // Initialize is idempotent for our purposes: it rebinds the store and repaints.
            _active.Initialize(_store);
            _active.Anchor = _anchor;

            _host.ShowView(_active);
            _host.HighlightTab(which);
            RefreshPeriodLabel();
        }

        /// <summary>Repaints the active view and its period label. The one call every other feature
        /// makes after changing the store underneath it.</summary>
        internal void Refresh()
        {
            _active.Refresh();
            RefreshPeriodLabel();
        }

        internal void RefreshPeriodLabel() => _host.PeriodLabel = _active.PeriodLabel;

        internal void GoToday()
        {
            _anchor = DateTime.Today;
            _active.Anchor = _anchor;
            RefreshPeriodLabel();
        }

        internal void Move(int direction)
        {
            _anchor = _active.Step(_anchor, direction);
            _active.Anchor = _anchor;
            RefreshPeriodLabel();
        }

        /// <summary>"+ New" starts on whatever the view is showing, not always today: composing an
        /// appointment for a week you are looking at should not silently jump back to this week.</summary>
        internal void NewAtAnchor()
        {
            var day = _anchor.Date == DateTime.Today ? DateTime.Today : _anchor.Date;
            _editor.NewAt(day.AddHours(9));
        }

        /// <summary>Status text for the state the store came up in, in order of how much it tells
        /// the user. Null when there is nothing worth saying.</summary>
        internal string? OpenStatus()
        {
            if (_store.MigratedCount > 0)
                return string.Format(_host.Loc("Str_Status_Migrated"), _store.MigratedCount);
            if (_store.LoadError != null)
                return string.Format(_host.Loc("Str_Status_LoadError"), _store.LoadError);
            return null;
        }
    }
}
