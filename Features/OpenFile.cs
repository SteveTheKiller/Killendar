using System;
using System.IO;
using System.Windows;
using Killendar.Services;

// Receiving a double-clicked .kcal. App registers the association and captures the path; this
// routes it once the window exists and a Killendar is open.
//
// It COPIES the file into the data folder and switches to the copy, rather than opening it where
// it sits. Two reasons, and they are the same two the Killendars dialog's Load button gives:
// a Killendar is written to constantly, and SQLite over SMB is a well-known way to corrupt a
// database - so silently making someone's network share or Downloads folder the live store is not
// what a double-click should mean. KillerNotes does exactly this for a double-clicked .kndb, so
// the family already answered the question.
//
// The confirm dialog is not ceremony: the copy changes which Killendar is open, and doing that
// without asking would be a surprise to anyone who only wanted a look at the file.
namespace Killendar
{
    public partial class MainWindow
    {
        /// <summary>Routes a double-clicked .kcal after the active Killendar has opened. Internal:
        /// App also calls this for a path forwarded from a blocked second launch.</summary>
        internal void HandlePendingOpenFile()
        {
            string? path = App.PendingOpenFile;
            App.PendingOpenFile = null;
            if (path == null || !_store.IsOpen) return;

            // Already the open one - nothing to copy, nothing to ask.
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_store.FilePath),
                              StringComparison.OrdinalIgnoreCase))
                return;

            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Kc_AddHead"), Path.GetFileName(path)),
                Loc("Str_Kc_AddBody"),
                Loc("Str_Btn_Add"),
                Loc("Str_Btn_Cancel")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            string status = _security.AdoptFile(path);
            _calendar.Refresh();
            if (status.Length > 0) StatusText.Text = status;
        }
    }
}
