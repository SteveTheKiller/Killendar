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
                _calendar.GoTo(day);
                ((ICalendarHost)this).ShowDayAgenda(day, null);
            }
            else if (dlg.Chosen is Models.CalendarEvent ev)
            {
                _calendar.GoTo(ev.Start.Date);
                ((ICalendarHost)this).ShowDayAgenda(ev.Start.Date, ev);
            }
        }
    }
}
