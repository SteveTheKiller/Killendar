using System;
using System.Threading.Tasks;
using System.Windows;
using Killendar.Controls;

// The footer's portable badge: a PORTABLE label and an "Install <App>..." button, shown only when
// the exe is running from somewhere other than an install location.
//
// This is the UI half of the family install system. The ENGINE - IsPortable, InstallAndRelaunch,
// the /silent and machine-wide paths, shortcut creation, uninstall - lives in the host app's
// App.xaml.cs, because it runs before any window exists. The kit does not ship it; copy it from
// KillerNotes or Killendar, which have the current version. This file assumes only:
//
//     App.IsPortable()                                        -> bool
//     App.InstallAndRelaunch(bool wantDesktop, bool allUsers) -> bool, false if it did not happen
//     App.IsDemo                                              -> bool
//
// Everything an app adds lives in its own Shell/Install.Extras.cs through ConfigureInstall: the
// all-users checkbox (and locking it when a machine-wide copy already exists), async checks that
// can call the install off, flushing before the relaunch and reopening after a failure, its own
// install call (off the interface thread, or one that can defer the relaunch), and its own status
// line. AfterPortableBadgeRefreshed runs after every badge refresh.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        /// <summary>Shows the badge when running portable. Call from Loaded, and again after a
        /// failed install so the button comes back. Hidden in demo mode, so a marketing
        /// screenshot of an uninstalled build does not carry an Install button.</summary>
        private void RefreshPortableBadge()
        {
            bool show = App.IsPortable() && !App.IsDemo;
            PortableBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            AfterPortableBadgeRefreshed();
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var options = new InstallOptions();
            ConfigureInstall(options);

            var dlg = options.OfferAllUsers
                ? new ConfirmDialog(
                    Loc("Str_Dlg_InstallMsg"),
                    Loc("Str_Dlg_InstallBullets"),
                    Loc("Str_Btn_DoInstall"),
                    Loc("Str_Btn_Cancel"),
                    check1Label: Loc("Str_Chk_Desktop"), check1Initial: true,
                    check2Label: Loc("Str_Chk_AllUsers"), check2Initial: false) { Owner = this }
                : new ConfirmDialog(
                    Loc("Str_Dlg_InstallMsg"),
                    Loc("Str_Dlg_InstallBullets"),
                    Loc("Str_Btn_DoInstall"),
                    Loc("Str_Btn_Cancel"),
                    check1Label: Loc("Str_Chk_Desktop"), check1Initial: true) { Owner = this };
            if (options.OfferAllUsers && options.AllUsersLockNote != null)
                dlg.LockCheck2(options.AllUsersLockNote);
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;
            if (options.CanInstallAsync != null && !await options.CanInstallAsync()) return;

            PortableBadge.Visibility = Visibility.Collapsed;

            // FLUSH FIRST. The install relaunches from the new location, so this process is about
            // to end: write out whatever is unsaved, and release any file handle the installed
            // copy is about to want (Killendar closes its SQLite store here).
            options.BeforeInstall?.Invoke();

            bool desktop = dlg.Check1Checked;
            bool allUsers = options.OfferAllUsers && dlg.Check2Checked;
            InstallOutcome outcome = options.InstallAsync != null
                ? await options.InstallAsync(desktop, allUsers)
                : App.InstallAndRelaunch(wantDesktop: desktop, allUsers: allUsers)
                    ? InstallOutcome.Installed : InstallOutcome.Failed;
            if (outcome == InstallOutcome.Installed) return;

            // Elevation refused, the copy failed or the relaunch waits. Put the badge back and
            // reopen anything that was closed above, so the session carries on.
            RefreshPortableBadge();
            options.AfterInstallFailed?.Invoke();
            string status = Loc(outcome == InstallOutcome.Deferred
                ? "Str_Status_RelaunchDeferred" : "Str_Status_InstallFailed");
            if (options.ShowStatus != null) options.ShowStatus(status);
            else StatusText.Text = status;
        }

        /// <summary>How an install attempt ended. Deferred means the copy is installed but the
        /// relaunch waits, which only an app supplying InstallAsync reports.</summary>
        private enum InstallOutcome { Installed, Failed, Deferred }

        /// <summary>The app's install behavior, filled in by ConfigureInstall. Every member is
        /// optional; left alone, the badge offers the desktop shortcut only.</summary>
        private sealed class InstallOptions
        {
            /// <summary>Adds the all-users checkbox (Str_Chk_AllUsers), unticked.</summary>
            public bool OfferAllUsers { get; set; }

            /// <summary>Ticks and locks the all-users checkbox with this note.</summary>
            public string? AllUsersLockNote { get; set; }

            /// <summary>Runs after the confirm; false calls the install off.</summary>
            public Func<Task<bool>>? CanInstallAsync { get; set; }

            /// <summary>Runs on the interface thread just before the install.</summary>
            public Action? BeforeInstall { get; set; }

            /// <summary>Replaces App.InstallAndRelaunch(desktop, allUsers).</summary>
            public Func<bool, bool, Task<InstallOutcome>>? InstallAsync { get; set; }

            /// <summary>Runs after a failed or deferred install, once the badge is back.</summary>
            public Action? AfterInstallFailed { get; set; }

            /// <summary>Replaces writing the failure line to StatusText.</summary>
            public Action<string>? ShowStatus { get; set; }
        }

        /// <summary>App hook: fill in the app's install behavior.</summary>
        partial void ConfigureInstall(InstallOptions options);

        /// <summary>App hook: runs after every badge refresh, for example to name the badge for
        /// screen readers.</summary>
        partial void AfterPortableBadgeRefreshed();
    }
}
