using System;
using System.Windows;
using System.Windows.Controls;
using Killendar.Models;
using Killendar.Services;
using Killendar.Views;
using Microsoft.Win32;

// The Killendar - calendar surface. Partial of MainWindow: owns the event store, the four views,
// the navigation strip and ICS import/export. Phase 3 hangs the appointment sidebar off the
// EventSelected / SlotSelected events raised here.
namespace Killendar
{
    public partial class MainWindow
    {
        private EventStore _store = null!;

        private MonthView _monthView = null!;
        private WeekView _weekView = null!;
        private DayView _dayView = null!;
        private AgendaView _agendaView = null!;

        private ICalendarView _active = null!;
        private DateTime _anchor = DateTime.Today;

        /// <summary>Called from the constructor once the XAML tree exists. Builds the views but
        /// does NOT open the Killendar - see OpenCalendarData for why that has to wait.</summary>
        private void InitCalendar()
        {
            _store = new EventStore();
            _store.Changed += () => _active.Refresh();

            _monthView  = new MonthView();
            _weekView   = new WeekView();
            _dayView    = new DayView();
            _agendaView = new AgendaView();

            foreach (ICalendarView v in new ICalendarView[] { _monthView, _weekView, _dayView, _agendaView })
            {
                v.EventSelected += OnEventSelected;
                v.SlotSelected  += OnSlotSelected;
            }

            SelectView("Month");   // paints an empty month; OpenCalendarData refreshes it
            UpdateLockGlyph();     // so the title-bar button is never blank before the open
        }

        /// <summary>
        /// Opens the active Killendar and repaints. Deliberately NOT called from the constructor:
        /// an encrypted Killendar prompts for its password, and a modal dialog needs Owner set to
        /// a window that has already been shown - doing it in the constructor throws "Cannot set
        /// Owner property to a Window that has not been shown previously". Cancelling the prompt
        /// also calls Close(), and a reentrant Close() inside Show() throws as well, which is why
        /// MainWindow dispatches this at Background priority from Loaded rather than calling it
        /// inline. KillerNotes carries the same comment for the same two crashes.
        /// </summary>
        private void OpenCalendarData()
        {
            OpenKillendar(exitOnCancel: true);   // Security.cs
            _active.Refresh();
            UpdatePeriodLabel();

            // Precedence: a message Security.cs parked (the fresh-Killendar escape hatch), then a
            // one-off migration count, then an error. Each is more informative than the next.
            if (_pendingStatus != null) { StatusText.Text = _pendingStatus; _pendingStatus = null; }
            else if (_store.MigratedCount > 0)
                StatusText.Text = string.Format(Loc("Str_Status_Migrated"), _store.MigratedCount);
            else if (_store.LoadError != null)
                StatusText.Text = string.Format(Loc("Str_Status_LoadError"), _store.LoadError);
        }

        private void SelectView(string which)
        {
            _active = which switch
            {
                "Week"   => _weekView,
                "Day"    => _dayView,
                "Agenda" => _agendaView,
                _        => _monthView
            };

            // Initialize is idempotent for our purposes: it just rebinds the store and repaints.
            _active.Initialize(_store);
            _active.Anchor = _anchor;

            ViewHost.Content = _active;
            HighlightTab(which);
            UpdatePeriodLabel();
        }

        /// <summary>The active tab carries the accent; the rest sit flat.</summary>
        private void HighlightTab(string which)
        {
            foreach (var (btn, tag) in new[]
                     {
                         (TabMonth, "Month"), (TabWeek, "Week"), (TabDay, "Day"), (TabAgenda, "Agenda")
                     })
            {
                if (tag == which) btn.SetResourceReference(ForegroundProperty, "PrimaryBrush");
                else              btn.SetResourceReference(ForegroundProperty, "TextBrush");
            }
        }

        private void UpdatePeriodLabel() => PeriodLabel.Text = _active.PeriodLabel;

        private void ViewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag) SelectView(tag);
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e)  => Move(-1);
        private void NextBtn_Click(object sender, RoutedEventArgs e)  => Move(1);

        private void TodayBtn_Click(object sender, RoutedEventArgs e)
        {
            _anchor = DateTime.Today;
            _active.Anchor = _anchor;
            UpdatePeriodLabel();
        }

        private void Move(int direction)
        {
            _anchor = _active.Step(_anchor, direction);
            _active.Anchor = _anchor;
            UpdatePeriodLabel();
        }

        // ---- appointment hooks (Appointments.cs owns the sidebar itself) ----

        private void OnEventSelected(CalendarEvent ev) => OpenAppointment(ev);

        private void OnSlotSelected(DateTime when) => OpenNewAppointment(when);

        /// <summary>"+ New" starts on whatever the view is showing, not always today: composing an
        /// appointment for a week you are looking at should not silently jump back to this week.</summary>
        private void NewEventBtn_Click(object sender, RoutedEventArgs e)
        {
            var day = _anchor.Date == DateTime.Today ? DateTime.Today : _anchor.Date;
            OpenNewAppointment(day.AddHours(9));
        }

        // ---- ICS ----

        private void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            // Themed picker rather than Microsoft.Win32.OpenFileDialog, which ignores the theme.
            var dlg = new FileDialog(FileDialogMode.Open)
            {
                Title = Loc("Str_Dlg_ImportTitle"),
                Filter = Loc("Str_Dlg_IcsFilter"),
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var incoming = IcsService.ParseFile(dlg.FileName);
                int added = _store.ImportEvents(incoming);
                int skipped = incoming.Count - added;
                StatusText.Text = skipped > 0
                    ? string.Format(Loc("Str_Status_ImportedSkipped"), added, skipped)
                    : string.Format(Loc("Str_Status_Imported"), added);
                _active.Refresh();
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Loc("Str_Status_ImportFailed"), ex.Message);
            }
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_store.Events.Count == 0)
            {
                StatusText.Text = Loc("Str_Status_NothingToExport");
                return;
            }

            if (!ConfirmPlaintextExport()) return;

            var dlg = new FileDialog(FileDialogMode.Save)
            {
                Title = Loc("Str_Dlg_ExportTitle"),
                Filter = Loc("Str_Dlg_IcsSaveFilter"),
                FileName = $"killendar-{DateTime.Today:yyyy-MM-dd}.ics",
                DefaultExt = ".ics"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                IcsService.ExportToFile(_store.Events, dlg.FileName);
                StatusText.Text = string.Format(Loc("Str_Status_Exported"), _store.Events.Count);
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Loc("Str_Status_ExportFailed"), ex.Message);
            }
        }

        /// <summary>
        /// An .ics file is plain text, so exporting from an encrypted Killendar hands out
        /// everything the password was protecting. Warn once before the file picker - the point
        /// is whether to export at all, not where to put it - with a "Don't remind me again"
        /// box. Plaintext Killendars never see this: there is nothing to undo.
        /// Returns false only if the user actively cancels.
        /// </summary>
        private bool ConfirmPlaintextExport()
        {
            if (!_store.HasPassword) return true;
            if (Settings.Get(SuppressExportWarningKey) == "1") return true;

            var dlg = new ConfirmDialog(
                Loc("Str_Exp_WarnHead"),
                Loc("Str_Exp_WarnBody"),
                Loc("Str_Btn_Export"),
                Loc("Str_Btn_Cancel"),
                check1Label: Loc("Str_Exp_WarnCheck"))
            { Owner = this };
            dlg.ShowDialog();

            // Only remember the choice if they went through with the export. Ticking the box and
            // then cancelling means "not this time", not "never warn me again".
            if (!dlg.Confirmed) return false;
            if (dlg.Check1Checked) Settings.Set(SuppressExportWarningKey, "1");
            return true;
        }

        private const string SuppressExportWarningKey = "SuppressPlaintextExportWarning";
    }
}
