namespace Killendar.Features
{
    /// <summary>
    /// What the calendar surface needs from the window beyond the shared shell services: somewhere
    /// to put the active view, the period label, and the tab highlight.
    /// </summary>
    internal interface ICalendarHost : IShellServices
    {
        /// <summary>The "July 2026" label above the grid.</summary>
        string PeriodLabel { set; }

        /// <summary>Puts the given view in the content area.</summary>
        void ShowView(object view);

        /// <summary>Marks one of Month / Week / Day / Agenda as the active tab.</summary>
        void HighlightTab(string which);

        void ShowFineMonthNavigation(bool visible);

        /// <summary>
        /// Ctrl+wheel over a time grid asked for a density step (+1 or -1). The shell owns the
        /// setting because it is persisted and shared by Week and Day, and the rail button reaches
        /// the same code.
        /// </summary>
        void StepDensity(int direction);

        /// <summary>
        /// Opens the sidebar showing DAY's appointments - viewing first, editing behind each
        /// row's Edit action (2026-07-30). <paramref name="highlight"/> marks the
        /// appointment that was clicked, or null when a bare day was.
        /// </summary>
        void ShowDayAgenda(System.DateTime day, Killendar.Models.CalendarEvent? highlight);
    }
}
