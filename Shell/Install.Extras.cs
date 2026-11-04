using System.Threading.Tasks;
using Killendar.Services;

namespace Killendar.Shell
{
    /// <summary>
    /// APP-OWNED: Killendar's side of the family install badge (Shell/Install.cs). The confirm
    /// carries the all-users checkbox, locked when a machine-wide copy already exists, and the
    /// calendar store is closed before the relaunch and reopened if the install fails.
    /// </summary>
    public partial class MainWindow
    {
        partial void ConfigureInstall(InstallOptions options)
        {
            options.OfferAllUsers = true;
            // Already installed for all users (by an admin, winget, choco or an RMM)? Then this
            // updates that copy in place, so the all-users box is ticked and locked with a note.
            if (App.MachineInstallExists())
                options.AllUsersLockNote = LocaleManager.Loc("Str_Install_AlreadyAllUsers");
            // The store holds a SQLite handle on the open Killendar, and the pooled file handle
            // has to be released before a second process opens the same file (two writers on one
            // .kcal is the failure mode single-instance exists to prevent).
            // The install ends in a shutdown that OnClosing no longer asks about, so a pending
            // appointment draft is asked about first; keeping it calls the install off.
            options.CanInstallAsync = () => Task.FromResult(ConfirmDraftBeforeExit());
            options.BeforeInstall = () => _store.Close();
            // The all-users install waits for its elevated child off the interface thread, so the
            // window keeps answering messages (the child's environment broadcast among them). The
            // store is closed by then, so the window takes no input until the install ends.
            options.InstallAsync = async (desktop, allUsers) =>
            {
                IsEnabled = false;
                try
                {
                    return await App.InstallAndRelaunchAsync(desktop, allUsers)
                        ? InstallOutcome.Installed : InstallOutcome.Failed;
                }
                finally { IsEnabled = true; }
            };
            // Reopen, so the session carries on rather than being left with a closed store.
            options.AfterInstallFailed = OpenCalendarData;
            // SetStatus is an explicit IShellServices implementation, so it is not callable
            // unqualified from inside MainWindow.
            options.ShowStatus = text => ((Features.IShellServices)this).SetStatus(text);
        }
    }
}
