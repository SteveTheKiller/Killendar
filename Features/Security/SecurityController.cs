using System;
using System.IO;
using Killendar.Controls;
using Microsoft.Data.Sqlite;
using Killendar.Services;

namespace Killendar.Features
{
    /// <summary>
    /// Password protection and the Killendar file lifecycle: unlock on launch, the title-bar lock
    /// button (set / change / remove), switching between Killendars, and the unlock screen's
    /// "New Killendar" escape hatch.
    ///
    /// A forgotten password can never be recovered - that is what the encryption is for - but the
    /// app must not become a brick, so the locked file is renamed aside and kept, never deleted.
    /// </summary>
    internal sealed class SecurityController
    {
        private readonly ISecurityHost _host;
        private readonly EventStore _store;

        /// <summary>Password of the open Killendar, kept for the session so switching files and
        /// coming back does not re-prompt for one already given.</summary>
        private string? _password;

        /// <summary>Status text produced before the views have painted; the shell collects it once
        /// the first paint is done.</summary>
        private string? _pendingStatus;

        internal SecurityController(ISecurityHost host, EventStore store)
        {
            _host = host;
            _store = store;
        }

        /// <summary>Drops the remembered password. Call when switching to a different file, whose
        /// password is not ours to try.</summary>
        internal void ForgetPassword() => _password = null;

        /// <summary>Returns and clears any queued status message.</summary>
        internal string? TakePendingStatus()
        {
            var s = _pendingStatus;
            _pendingStatus = null;
            return s;
        }

        /// <summary>Opens the active Killendar, prompting to unlock when it is encrypted. False
        /// when the user cancels; with exitOnCancel the host window closes instead.</summary>
        internal bool Open(bool exitOnCancel)
        {
            try
            {
                _store.Prepare();   // creates the data folder, migrates a pre-database events.json

                if (EventStore.ActiveIsEncrypted())
                {
                    // Try the session password silently first.
                    if (_password != null)
                    {
                        try { _store.Open(EventStore.ActivePath, _password); }
                        catch (SqliteException) { }
                    }
                    if (!_store.IsOpen && !PromptUnlock(exitOnCancel)) return false;
                }
                else
                {
                    _store.Open(EventStore.ActivePath);
                    _password = null;
                }
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_Status_LoadError"), ex.Message));
                return false;
            }

            RefreshLockState();
            RefreshActiveLabel();
            return true;
        }

        private bool PromptUnlock(bool exitOnCancel)
        {
            string heading = _host.Loc("Str_Pw_UnlockHead");
            while (true)
            {
                var dlg = new PasswordDialog(
                    heading,
                    string.Format(_host.Loc("Str_Pw_Protected"), Path.GetFileName(EventStore.ActivePath)),
                    _host.Loc("Str_Btn_Unlock"),
                    extraText: _host.Loc("Str_Pw_NewKcalBtn")) { Owner = _host.Window };
                dlg.ShowDialog();

                if (dlg.ExtraClicked)
                {
                    if (StartFresh()) return true;
                    continue;   // declined the confirm - back to the unlock prompt
                }
                if (!dlg.Confirmed)
                {
                    if (exitOnCancel) _host.Window.Close();
                    return false;
                }
                try
                {
                    _store.Open(EventStore.ActivePath, dlg.Password);
                    _password = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password;
                    return true;
                }
                catch (SqliteException) { heading = _host.Loc("Str_Pw_WrongPw"); }
            }
        }

        /// <summary>The escape hatch for a forgotten password. The appointments in the locked file
        /// are not recoverable, but the file itself is renamed aside and kept.</summary>
        private bool StartFresh()
        {
            var confirm = new ConfirmDialog(
                _host.Loc("Str_Dlg_FreshHead"),
                _host.Loc("Str_Dlg_FreshBody"),
                _host.Loc("Str_Btn_StartNew"),
                _host.Loc("Str_Btn_Cancel")) { Owner = _host.Window };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return false;

            string archived = EventStore.ArchiveActive();
            _store.Open(EventStore.ActivePath);
            _password = null;
            _pendingStatus = string.Format(_host.Loc("Str_Status_FreshKcal"), Path.GetFileName(archived));
            return true;
        }

        /// <summary>
        /// The Manage Killendars dialog. The store is closed for its whole lifetime so any file -
        /// the active one included - can be renamed or deleted safely, then reopened afterwards.
        /// </summary>
        internal void ShowKillendars()
        {
            // The panel is NOT closed here. It used to be, unconditionally, which meant merely
            // LOOKING at the Killendars list threw away whatever you were composing - and on a
            // narrow window it also gave back the width the panel had borrowed, so the main window
            // resized itself behind the dialog. (2026-07-30)
            //
            // It is closed further down, and only if the active file actually changed: a half-typed
            // appointment belongs to the store it was started in, and saving it into a different
            // Killendar would be wrong. Canceling, or picking the file already open, leaves the
            // panel exactly as it was.
            string previous = EventStore.ActiveFile;
            string previousDir = EventStore.DataDir;
            string previousPath = EventStore.ActivePath;
            _store.Close();

            var dlg = new KillendarsDialog { Owner = _host.Window };
            dlg.ShowDialog();

            if (dlg.Selected != null &&
                !string.Equals(dlg.Selected, EventStore.ActiveFile, StringComparison.OrdinalIgnoreCase))
            {
                EventStore.SetActive(dlg.Selected);
                _password = null;
            }

            if (!Open(exitOnCancel: false))
            {
                // Unlocking the chosen Killendar was canceled - fall back to the previous one
                // rather than leaving the app with nothing open.
                EventStore.SetDataDir(previousDir);
                EventStore.SetActive(previous);
                _password = null;
                Open(exitOnCancel: false);
            }

            // Only now, and only if we actually landed on a different Killendar - see the note at
            // the top. Anything being composed belongs to the store it was started in.
            if (!string.Equals(EventStore.ActivePath, previousPath, StringComparison.OrdinalIgnoreCase))
                _host.CloseSidebar();

            _host.RefreshView();
            RefreshActiveLabel();
            _host.SetStatus(TakePendingStatus()
                            ?? string.Format(_host.Loc("Str_Status_Opened"), _store.DisplayName));
        }

        /// <summary>Adopts an external .kcal: copies it into the data folder, switches to the copy
        /// and opens it, falling back to what was open before if that is canceled or fails.
        /// Returns the status text to show.</summary>
        internal string AdoptFile(string path)
        {
            _host.CloseSidebar();

            string previous = EventStore.ActiveFile;
            try
            {
                string name = EventStore.ImportKillendar(path);
                _store.Close();
                EventStore.SetActive(name);
                _password = null;

                if (!Open(exitOnCancel: false))
                {
                    EventStore.SetActive(previous);
                    _password = null;
                    Open(exitOnCancel: false);
                    _host.RefreshView();
                    RefreshActiveLabel();
                    return string.Empty;
                }

                _host.RefreshView();
                RefreshActiveLabel();
                return string.Format(_host.Loc("Str_Status_Opened"), _store.DisplayName);
            }
            catch (Exception ex)
            {
                // The copy failed before anything switched, or the switch failed after it - either
                // way, get a Killendar open again before the next edit needs one.
                if (!_store.IsOpen)
                {
                    EventStore.SetActive(previous);
                    Open(exitOnCancel: false);
                    _host.RefreshView();
                    RefreshActiveLabel();
                }
                return string.Format(_host.Loc("Str_Kc_LoadFailed"), ex.Message);
            }
        }

        /// <summary>Adopts a double-clicked .kcal once a Killendar is open, asking first.
        ///
        /// It COPIES the file into the data folder and switches to the copy rather than opening it
        /// where it sits. A Killendar is written to constantly, and SQLite over SMB is a well-known
        /// way to corrupt a database, so silently making someone's network share or Downloads folder
        /// the live store is not what a double-click should mean.
        ///
        /// The confirm is not ceremony: the copy changes which Killendar is open, and doing that
        /// without asking would surprise anyone who only wanted a look at the file.</summary>
        internal void AdoptPendingFile()
        {
            string? path = App.PendingOpenFile;
            App.PendingOpenFile = null;
            if (path == null || !_store.IsOpen) return;

            // Already the open one - nothing to copy, nothing to ask.
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_store.FilePath),
                              StringComparison.OrdinalIgnoreCase))
                return;

            var confirm = new ConfirmDialog(
                string.Format(_host.Loc("Str_Kc_AddHead"), Path.GetFileName(path)),
                _host.Loc("Str_Kc_AddBody"),
                _host.Loc("Str_Btn_Add"),
                _host.Loc("Str_Btn_Cancel")) { Owner = _host.Window };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            string status = AdoptFile(path);
            _host.RefreshView();
            if (status.Length > 0) _host.SetStatus(status);
        }

        /// <summary>Sets, changes or removes the password on the open Killendar.</summary>
        internal void ToggleLock()
        {
            if (!_store.IsOpen) return;

            try
            {
                if (!_store.HasPassword)
                {
                    var dlg = new PasswordDialog(
                        _host.Loc("Str_Pw_SetHead"),
                        _host.Loc("Str_Pw_SetBody"),
                        _host.Loc("Str_Btn_Encrypt"),
                        showConfirm: true, showHint: true) { Owner = _host.Window };
                    dlg.ShowDialog();
                    if (!dlg.Confirmed || string.IsNullOrEmpty(dlg.Password)) return;
                    if (dlg.Password != dlg.PasswordConfirm)
                    {
                        _host.SetStatus(_host.Loc("Str_Status_PwMismatch"));
                        return;
                    }
                    _store.SetPassword(dlg.Password);
                    _password = dlg.Password;
                    _host.SetStatus(_host.Loc("Str_Status_Encrypted"));
                }
                else
                {
                    // An empty box on the change prompt means "remove the password", which is why
                    // this path does not reject a blank entry the way the set path does.
                    var dlg = new PasswordDialog(
                        _host.Loc("Str_Pw_ChangeHead"),
                        _host.Loc("Str_Pw_ChangeBody"),
                        _host.Loc("Str_Btn_Apply"),
                        showConfirm: true, showHint: true) { Owner = _host.Window };
                    dlg.ShowDialog();
                    if (!dlg.Confirmed) return;
                    if (dlg.Password != dlg.PasswordConfirm)
                    {
                        _host.SetStatus(_host.Loc("Str_Status_PwMismatch"));
                        return;
                    }
                    _store.SetPassword(string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password);
                    _password = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password;
                    _host.SetStatus(_host.Loc(_store.HasPassword
                        ? "Str_Status_PwChanged" : "Str_Status_PwRemoved"));
                }
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_Status_PwFailed"), ex.Message));
            }
            RefreshLockState();
        }

        internal void RefreshLockState() => _host.ShowLockState(_store.HasPassword);

        /// <summary>The label is hidden when there is only the default Killendar and nothing is
        /// encrypted - the common case does not need the noise.</summary>
        internal void RefreshActiveLabel()
        {
            string name = _store.DisplayName;
            bool onlyDefault = string.Equals(EventStore.ActiveFile, EventStore.DefaultFileName,
                                             StringComparison.OrdinalIgnoreCase)
                               && EventStore.ListKillendars().Count <= 1;
            _host.ShowActiveKillendar(name, !(string.IsNullOrEmpty(name) || onlyDefault));
        }

        /// <summary>Reopens the active file from disk so changes made by another program become
        /// visible immediately. The session password is retained for encrypted Killendars.</summary>
        internal void ReloadActive()
        {
            _host.CloseSidebar();
            _store.Close();
            if (!Open(exitOnCancel: false)) return;
            _host.RefreshView();
            _host.SetStatus(_host.Loc("Str_Status_Reloaded"));
        }
    }
}
