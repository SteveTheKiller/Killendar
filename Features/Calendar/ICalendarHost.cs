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
    }
}
