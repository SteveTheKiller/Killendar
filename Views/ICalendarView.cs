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

        /// <summary>Heading for the current period, e.g. "July 2026" or "Jul 27 - Aug 2, 2026".</summary>
        string PeriodLabel { get; }

        void Refresh();

        /// <summary>Anchor moved one period forward (direction 1) or back (-1).</summary>
        DateTime Step(DateTime from, int direction);

        /// <summary>An existing appointment was clicked. Phase 3 opens it in the sidebar.</summary>
        event Action<CalendarEvent> EventSelected;

        /// <summary>Empty space was clicked, carrying the date and time under the cursor.</summary>
        event Action<DateTime> SlotSelected;
    }
}
