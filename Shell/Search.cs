using System.Windows;
using Killendar.Controls;
using Killendar.Features;

// Search (Ctrl+F, the toolbar's Search button) and go to a date (Ctrl+G) share one box
// (Controls/SearchDialog). A found appointment opens its day in the panel with it marked; a date
// moves the calendar there.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void SearchBtn_Click(object sender, RoutedEventArgs e) => OpenSearch();

        private void OpenSearch()
        {
            if (!_store.IsOpen || _security.IsLocked) return;
            var dlg = new SearchDialog(_store) { Owner = this };
            dlg.ShowDialog();
            if (dlg.GoTo is System.DateTime day)
            {
                SearchOpen.Run(_appointments.ConfirmReplace, () => _calendar.GoTo(day), () => ShowDayAgenda(day, null));
            }
            else if (dlg.Chosen is Models.CalendarEvent ev)
            {
                SearchOpen.Run(_appointments.ConfirmReplace, () => _calendar.GoTo(ev.Start.Date), () => ShowDayAgenda(ev.Start.Date, ev.Id));
            }
        }
    }

    /// <summary>Search asks before it replaces a draft, and only then moves the calendar: a kept
    /// draft would otherwise sit beside a calendar that had already moved to the result.</summary>
    internal static class SearchOpen
    {
        /// <summary>True when the result was shown; false when the user chose to keep the draft,
        /// in which case nothing moved.</summary>
        internal static bool Run(System.Func<bool> confirmReplace, System.Action goTo, System.Action show)
        {
            if (!confirmReplace()) return false;
            goTo();
            show();
            return true;
        }
    }
}
