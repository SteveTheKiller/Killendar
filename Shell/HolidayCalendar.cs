using Killendar.Controls;
using Killendar.Services;

// The holiday calendar chosen in the date settings (Holidays.cs). An encrypted one asks for its
// own password when chosen; the password is never saved, so after a restart or a lock it is
// chosen again to unlock it.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void InitHolidayCalendar()
        {
            _dateSettingsMenu._holidayChosen = ChooseHolidayCalendar;
            Holidays.Changed += () => _calendar?.Refresh();
        }

        private void ChooseHolidayCalendar(string name)
        {
            Holidays.Choose(name, _store, EventStore.ActiveFile);
            if (Holidays.Status != Holidays.State.Locked) return;
            var dlg = new PasswordDialog(Loc("Str_Pw_UnlockHead"),
                string.Format(Loc("Str_Pw_Protected"), ShownCalendars.DisplayName(name)),
                Loc("Str_Shown_Unlock")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed || string.IsNullOrEmpty(dlg.Password)) return;
            if (!Holidays.Unlock(dlg.Password)) StatusText.Text = Loc("Str_Pw_WrongPw");
        }
    }
}
