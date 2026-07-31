using System.Windows;
using Killendar.Controls;
using Killendar.Services;

namespace Killendar.Shell
{
    /// <summary>
    /// The UI half of the family install system. The engine is all in App.xaml.cs (IsPortable,
    /// InstallAndRelaunch, the silent and machine-wide paths); this is the footer badge that
    /// offers it, matching KillerNotes, KillerScan and KillerShell.
    ///
    /// Killendar's InstallAndRelaunch takes an all-users flag as well as the desktop shortcut,
    /// so the confirm carries two checkboxes rather than KillerNotes' one.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Shows the badge when running from outside the installed location. Hidden in
        /// demo mode so marketing screenshots stay clean.</summary>
        private void RefreshPortableBadge()
        {
            bool show = App.IsPortable() && !App.IsDemo;
            PortableBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ConfirmDialog(
                LocaleManager.Loc("Str_Dlg_InstallMsg"),
                LocaleManager.Loc("Str_Dlg_InstallBullets"),
                LocaleManager.Loc("Str_Btn_DoInstall"),
                LocaleManager.Loc("Str_Btn_Cancel"),
                check1Label: LocaleManager.Loc("Str_Chk_Desktop"),   check1Initial: true,
                check2Label: LocaleManager.Loc("Str_Chk_AllUsers"),  check2Initial: false)
            { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            // The install relaunches from the new location, so this process is about to end.
            // Close the store first: it holds a SQLite handle on the open Killendar, and the
            // pooled file handle has to be released before a second process opens the same file
            // (two writers on one .kcal is the failure mode single-instance exists to prevent).
            PortableBadge.Visibility = Visibility.Collapsed;
            _store.Close();

            if (!App.InstallAndRelaunch(wantDesktop: dlg.Check1Checked, allUsers: dlg.Check2Checked))
            {
                // Elevation refused, or the copy failed. Put the badge back and reopen, so the
                // session carries on rather than being left with a closed store.
                RefreshPortableBadge();
                OpenCalendarData();
                // SetStatus is an explicit IShellServices implementation, so it is not callable
                // unqualified from inside MainWindow.
                ((Features.IShellServices)this).SetStatus(LocaleManager.Loc("Str_Status_InstallFailed"));
            }
        }
    }
}
