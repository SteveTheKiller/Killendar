using System;
using System.Text;
using Killendar.Controls;
using Killendar.Services;

namespace Killendar.Features
{
    /// <summary>
    /// iCalendar import and export, plus the warning that an export leaves an encrypted Killendar's
    /// contents sitting in a plain text file.
    /// </summary>
    internal sealed class IcsTransfer
    {
        private const string SuppressExportWarningKey = "SuppressPlaintextExportWarning";

        private readonly ICalendarHost _host;
        private readonly EventStore _store;
        private readonly Action _afterChange;

        internal IcsTransfer(ICalendarHost host, EventStore store, Action afterChange)
        {
            _host = host;
            _store = store;
            _afterChange = afterChange;
        }

        internal void Import()
        {
            // Themed picker rather than Microsoft.Win32.OpenFileDialog, which ignores the theme.
            var dlg = new FileDialog(FileDialogMode.Open)
            {
                Title = _host.Loc("Str_Dlg_ImportTitle"),
                Filter = _host.Loc("Str_Dlg_IcsFilter"),
                CheckFileExists = true
            };
            if (dlg.ShowDialog(_host.Window) != true) return;

            try
            {
                var parsed  = IcsService.ParseFile(dlg.FileName);
                int added   = _store.ImportEvents(parsed.Events);
                int skipped = parsed.Events.Count - added;

                var status = new StringBuilder(skipped > 0
                    ? string.Format(_host.Loc("Str_Status_ImportedSkipped"), added, skipped)
                    : string.Format(_host.Loc("Str_Status_Imported"), added));

                // Everything the file contained that Killendar could not keep is SAID, not
                // swallowed. A repeating appointment in particular imports as a single date, and
                // the user has no way of discovering that except by missing the second occurrence.
                Note(status, parsed.Repeating,   "Str_Status_ImportRepeat");
                Note(status, parsed.Unreadable,  "Str_Status_ImportUnreadable");
                Note(status, parsed.Unsupported, "Str_Status_ImportUnsupported");

                _host.SetStatus(status.ToString());
                _afterChange();
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_Status_ImportFailed"), ex.Message));
            }
        }

        internal void Export()
        {
            if (_store.Events.Count == 0)
            {
                _host.SetStatus(_host.Loc("Str_Status_NothingToExport"));
                return;
            }

            if (!ConfirmPlaintextExport()) return;

            var dlg = new FileDialog(FileDialogMode.Save)
            {
                Title = _host.Loc("Str_Dlg_ExportTitle"),
                Filter = _host.Loc("Str_Dlg_IcsSaveFilter"),
                FileName = $"killendar-{DateTime.Today:yyyy-MM-dd}.ics",
                DefaultExt = ".ics"
            };
            if (dlg.ShowDialog(_host.Window) != true) return;

            try
            {
                IcsService.ExportToFile(_store.Events, dlg.FileName);
                _host.SetStatus(string.Format(_host.Loc("Str_Status_Exported"), _store.Events.Count));
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_Status_ExportFailed"), ex.Message));
            }
        }

        /// <summary>
        /// Appends one caveat sentence to the import status when its count is non-zero. No
        /// separator is added here: each locale's string carries its OWN leading punctuation,
        /// because the sentence break is "." in Latin scripts, "。" in Japanese and Chinese and
        /// "।" in Bengali. Hardcoding ". " would put a Western full stop in every language.
        /// </summary>
        private void Note(StringBuilder status, int count, string key)
        {
            if (count <= 0) return;
            status.Append(string.Format(_host.Loc(key), count));
        }

        /// <summary>
        /// An .ics file is plain text, so exporting from an encrypted Killendar hands out everything
        /// the password was protecting. Warned before the picker - the question is whether to export
        /// at all, not where to put it - with a "Don't remind me again" box. A plaintext Killendar
        /// never sees this: there is nothing to undo. False only when the user cancels.
        /// </summary>
        private bool ConfirmPlaintextExport()
        {
            if (!_store.HasPassword) return true;
            if (Settings.Get(SuppressExportWarningKey) == "1") return true;

            var dlg = new ConfirmDialog(
                _host.Loc("Str_Exp_WarnHead"),
                _host.Loc("Str_Exp_WarnBody"),
                _host.Loc("Str_Btn_Export"),
                _host.Loc("Str_Btn_Cancel"),
                check1Label: _host.Loc("Str_Exp_WarnCheck")) { Owner = _host.Window };
            dlg.ShowDialog();

            // Only remember the choice if the export actually went ahead. Ticking the box and then
            // cancelling means "not this time", not "never warn me again".
            if (!dlg.Confirmed) return false;
            if (dlg.Check1Checked) Settings.Set(SuppressExportWarningKey, "1");
            return true;
        }
    }
}
