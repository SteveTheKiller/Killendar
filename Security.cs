using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Killendar.Services;

// Password protection and the Killendar file lifecycle: unlock-on-launch, the title-bar lock
// button (set / change / remove), and the unlock screen's "New Killendar" escape hatch. A
// forgotten password can never be recovered - that is what the encryption is for - but the app
// must not become a brick, so the locked file is renamed aside and kept.
//
// Ported from KillerNotes' Security.cs. The differences: the store is an instance rather than a
// static, and there is no per-item locking - one Killendar is the whole unit.
namespace Killendar
{
    public partial class MainWindow
    {
        private string? _kcalPassword;    // password of the open Killendar, for silent reopens
        private string? _pendingStatus;   // status line to show once the views have painted

        /// <summary>Opens the active Killendar, prompting to unlock when it is encrypted. Returns
        /// false when the user cancels; with exitOnCancel the window closes instead.</summary>
        private bool OpenKillendar(bool exitOnCancel)
        {
            try
            {
                _store.Prepare();   // creates the data folder, migrates a pre-database events.json

                if (EventStore.ActiveIsEncrypted())
                {
                    // Try the session's known password silently first, so switching files and
                    // coming back never re-prompts for a password we already hold.
                    if (_kcalPassword != null)
                    {
                        try { _store.Open(EventStore.ActivePath, _kcalPassword); }
                        catch (SqliteException) { }
                    }
                    if (!_store.IsOpen && !PromptUnlock(exitOnCancel)) return false;
                }
                else
                {
                    _store.Open(EventStore.ActivePath);
                    _kcalPassword = null;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Loc("Str_Status_LoadError"), ex.Message);
                return false;
            }

            UpdateLockGlyph();
            UpdateActiveKillendarLabel();
            return true;
        }

        private bool PromptUnlock(bool exitOnCancel)
        {
            string heading = Loc("Str_Pw_UnlockHead");
            while (true)
            {
                var dlg = new PasswordDialog(
                    heading,
                    string.Format(Loc("Str_Pw_Protected"), Path.GetFileName(EventStore.ActivePath)),
                    Loc("Str_Btn_Unlock"),
                    extraText: Loc("Str_Pw_NewKcalBtn")) { Owner = this };
                dlg.ShowDialog();

                if (dlg.ExtraClicked)
                {
                    if (StartFreshKillendar()) return true;
                    continue;   // declined the confirm - back to the unlock prompt
                }
                if (!dlg.Confirmed)
                {
                    if (exitOnCancel) Close();
                    return false;
                }
                try
                {
                    _store.Open(EventStore.ActivePath, dlg.Password);
                    _kcalPassword = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password;
                    return true;
                }
                catch (SqliteException) { heading = Loc("Str_Pw_WrongPw"); }
            }
        }

        /// <summary>The escape hatch for a forgotten password. The appointments in the locked file
        /// are not recoverable - that is the point - but the file itself is renamed aside and kept,
        /// never deleted.</summary>
        private bool StartFreshKillendar()
        {
            var confirm = new ConfirmDialog(
                Loc("Str_Dlg_FreshHead"),
                Loc("Str_Dlg_FreshBody"),
                Loc("Str_Btn_StartNew"),
                Loc("Str_Btn_Cancel")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return false;

            string archived = EventStore.ArchiveActive();
            _store.Open(EventStore.ActivePath);
            _kcalPassword = null;
            _pendingStatus = string.Format(Loc("Str_Status_FreshKcal"), Path.GetFileName(archived));
            return true;
        }

        // ---- Manage Killendars (rail button) ----
        // The store is closed for the whole dialog, so every file - the active one included - can
        // be renamed or deleted safely. Reopening afterwards reuses the session password silently
        // where it still fits.

        private void KillendarsButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSidebar();   // Sidebar.cs - the appointment being edited may be about to vanish

            string previous = EventStore.ActiveFile;
            _store.Close();

            var dlg = new KillendarsDialog { Owner = this };
            dlg.ShowDialog();

            if (dlg.Selected != null &&
                !string.Equals(dlg.Selected, EventStore.ActiveFile, StringComparison.OrdinalIgnoreCase))
            {
                EventStore.SetActive(dlg.Selected);
                _kcalPassword = null;   // a different file - its password is not ours to try
            }

            if (!OpenKillendar(exitOnCancel: false))
            {
                // Unlocking the chosen Killendar was cancelled - fall back to the previous one
                // rather than leaving the app with nothing open.
                EventStore.SetActive(previous);
                _kcalPassword = null;
                OpenKillendar(exitOnCancel: false);
            }

            _active.Refresh();
            UpdateActiveKillendarLabel();
            if (_pendingStatus != null) { StatusText.Text = _pendingStatus; _pendingStatus = null; }
            else StatusText.Text = string.Format(Loc("Str_Status_Opened"), _store.DisplayName);
        }

        /// <summary>Footer label click - same destination as the rail button.</summary>
        private void ActiveKillendarLabel_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            KillendarsButton_Click(sender, new RoutedEventArgs());
        }

        /// <summary>Footer label showing which Killendar is open. Hidden when there is only the
        /// default one and nothing is encrypted - the common case does not need the noise.</summary>
        private void UpdateActiveKillendarLabel()
        {
            string name = _store.DisplayName;
            bool onlyDefault = string.Equals(EventStore.ActiveFile, EventStore.DefaultFileName,
                                             StringComparison.OrdinalIgnoreCase)
                               && EventStore.ListKillendars().Count <= 1;
            ActiveKillendarLabel.Text = name;
            ActiveKillendarLabel.Visibility = (string.IsNullOrEmpty(name) || onlyDefault)
                ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---- Lock button: set / change / remove the password ----

        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_store.IsOpen) return;

            try
            {
                if (!_store.HasPassword)
                {
                    var dlg = new PasswordDialog(
                        Loc("Str_Pw_SetHead"),
                        Loc("Str_Pw_SetBody"),
                        Loc("Str_Btn_Encrypt"),
                        showConfirm: true, showHint: true) { Owner = this };
                    dlg.ShowDialog();
                    if (!dlg.Confirmed || string.IsNullOrEmpty(dlg.Password)) return;
                    if (dlg.Password != dlg.PasswordConfirm)
                    {
                        StatusText.Text = Loc("Str_Status_PwMismatch");
                        return;
                    }
                    _store.SetPassword(dlg.Password);
                    _kcalPassword = dlg.Password;
                    StatusText.Text = Loc("Str_Status_Encrypted");
                }
                else
                {
                    // An empty box on the change prompt means "remove the password", which is why
                    // this path does not reject a blank entry the way the set path does.
                    var dlg = new PasswordDialog(
                        Loc("Str_Pw_ChangeHead"),
                        Loc("Str_Pw_ChangeBody"),
                        Loc("Str_Btn_Apply"),
                        showConfirm: true, showHint: true) { Owner = this };
                    dlg.ShowDialog();
                    if (!dlg.Confirmed) return;
                    if (dlg.Password != dlg.PasswordConfirm)
                    {
                        StatusText.Text = Loc("Str_Status_PwMismatch");
                        return;
                    }
                    _store.SetPassword(string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password);
                    _kcalPassword = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password;
                    StatusText.Text = Loc(_store.HasPassword ? "Str_Status_PwChanged" : "Str_Status_PwRemoved");
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Loc("Str_Status_PwFailed"), ex.Message);
            }
            UpdateLockGlyph();
        }

        /// <summary>Lock (0xE72E) when encrypted, unlock (0xE785) when plaintext, Segoe MDL2.
        /// Written as char casts so the private-use glyphs cannot be mangled by tooling - the same
        /// rule the rail chevrons follow.</summary>
        private void UpdateLockGlyph()
        {
            LockButton.Content = ((char)(_store.HasPassword ? 0xE72E : 0xE785)).ToString();
            LockButton.ToolTip = Loc(_store.HasPassword ? "Str_Tip_LockOn" : "Str_Tip_LockOff");
        }
    }
}
