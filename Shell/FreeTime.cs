using Killendar.Controls;
using Killendar.Services;

// Find open time (O): gaps across the shown calendars. A picked gap opens a new appointment at
// its start with the searched length, in the edit panel; nothing is saved until Save.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void OpenFreeTime()
        {
            if (!_store.IsOpen || _security.IsLocked) return;
            var dlg = new FreeTimeDialog((from, to) => ShownCalendars.With(_store.GetInRange(from, to), from, to)) { Owner = this };
            dlg.ShowDialog();
            if (dlg.Chosen is System.DateTime start && _appointments.ConfirmReplace())
            {
                _calendar.GoTo(start.Date);
                _appointments.NewFromQuickAdd(new QuickAddResult { Start = start, End = start + dlg.Length });
            }
        }
    }
}
