using System;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Views
{
    /// <summary>
    /// What MainWindow needs from a calendar view. Navigation lives in the window (one prev/next/today
    /// set for all four views); each view only says how far a step moves and what the period is called.
    /// </summary>
    internal interface ICalendarView
    {
        void Initialize(EventStore store);

        /// <summary>The date the view is centred on. Setting it repaints.</summary>
        DateTime Anchor { get; set; }

        /// <summary>
        /// What the appointment panel is currently talking about, or (null, null) when it is shut.
        /// The view marks it so there is always something on screen saying WHICH slot you are
        /// typing about, and it follows the date and time boxes, so correcting either moves the
        /// marker. (Steve, 2026-07-30.)
        ///
        /// Two parts rather than one DateTime because the time is genuinely optional: an all-day
        /// appointment, or a half-typed time box, has a day but no meaningful slot - and midnight
        /// is a real time, so it cannot double as "no time". Month uses only the day; Week and Day
        /// use both, and draw no time band when <paramref name="timeOfDay"/> is null.
        /// </summary>
        void SetSelection(DateTime? day, TimeSpan? timeOfDay);

        /// <summary>Heading for the current period, e.g. "July 2026" or "Jul 27 - Aug 2, 2026".</summary>
        string PeriodLabel { get; }

        void Refresh();

        /// <summary>Anchor moved one period forward (direction 1) or back (-1).</summary>
        DateTime Step(DateTime from, int direction);

        /// <summary>An existing appointment was clicked. Opens that day's agenda in the sidebar
        /// with the appointment highlighted (Steve, 2026-07-30 - edit is behind an Edit action,
        /// never the default).</summary>
        event Action<CalendarEvent> EventSelected;

        /// <summary>
        /// A DAY was clicked - a Month cell, a Week/Day column header, an Agenda day heading.
        /// Opens that day's agenda in the sidebar. Distinct from SlotSelected, which is an
        /// explicit CREATE gesture.
        /// </summary>
        event Action<DateTime> DaySelected;

        /// <summary>An explicit create: an empty half-hour slot in a time grid, or a context
        /// menu's add item, carrying the date and time under the cursor.</summary>
        event Action<DateTime> SlotSelected;

        /// <summary>
        /// Ctrl+wheel over the view, carrying +1 or -1. The shell owns the density setting (it is
        /// persisted and shared by Week and Day), so the view only reports the gesture.
        /// Views with nothing to scale never raise it.
        /// </summary>
        event Action<int> DensityStepped;
    }
}
