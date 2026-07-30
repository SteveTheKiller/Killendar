using System.Windows;
using System.Windows.Controls;
using Killendar.Features;
using Killendar.Services;

// MainWindow's side of the calendar surface: it satisfies ICalendarHost, wires the toolbar, and
// composes the feature objects. The behaviour lives in Features/Calendar/.
namespace Killendar.Shell
{
    public partial class MainWindow : ICalendarHost
    {
        private EventStore _store = null!;
        private CalendarController _calendar = null!;
        private IcsTransfer _ics = null!;

        // ---- composition root ----

        /// <summary>Called from the constructor once the XAML tree exists. Builds the store and the
        /// features and hands each the seams it needs. Deliberately the only place in the app that
        /// knows how the pieces fit together.</summary>
        private void InitCalendar()
        {
            _store = new EventStore();

            _appointments = new AppointmentEditor(this, _store);
            _security     = new SecurityController(this, _store);
            _calendar     = new CalendarController(this, _store, _appointments);
            _ics          = new IcsTransfer(this, _store, () => _calendar.Refresh());

            _store.Changed += () => _calendar.Refresh();

            _calendar.Initialize();         // builds the views, shows Month
            _security.RefreshLockState();   // so the title-bar lock is never blank before the open
        }

        /// <summary>
        /// Opens the active Killendar and repaints. Deliberately NOT called from the constructor: an
        /// encrypted Killendar prompts for its password, and a modal dialog needs an Owner that has
        /// already been shown, or it throws "Cannot set Owner property to a Window that has not been
        /// shown previously". Cancelling the prompt also calls Close(), and a reentrant Close()
        /// inside Show() throws as well - which is why MainWindow dispatches this at Background
        /// priority from Loaded rather than calling it inline.
        /// </summary>
        private void OpenCalendarData()
        {
            _security.Open(exitOnCancel: true);
            _calendar.Refresh();

            // Precedence: a message the security controller parked (the fresh-Killendar escape
            // hatch), then whatever state the store came up in.
            StatusText.Text = _security.TakePendingStatus()
                              ?? _calendar.OpenStatus()
                              ?? StatusText.Text;
        }

        // ---- ICalendarHost ----

        string ICalendarHost.PeriodLabel { set => PeriodLabel.Text = value; }

        void ICalendarHost.ShowView(object view) => ViewHost.Content = view;

        /// <summary>The active tab carries the accent; the rest sit flat.</summary>
        void ICalendarHost.HighlightTab(string which)
        {
            foreach (var (btn, tag) in new[]
                     {
                         (TabMonth, "Month"), (TabWeek, "Week"), (TabDay, "Day"), (TabAgenda, "Agenda")
                     })
            {
                btn.SetResourceReference(ForegroundProperty,
                                         tag == which ? "PrimaryBrush" : "TextBrush");
            }
        }

        // ---- toolbar ----

        private void ViewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag) _calendar.SelectView(tag);
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e) => _calendar.Move(-1);

        private void NextBtn_Click(object sender, RoutedEventArgs e) => _calendar.Move(1);

        private void TodayBtn_Click(object sender, RoutedEventArgs e) => _calendar.GoToday();

        private void NewEventBtn_Click(object sender, RoutedEventArgs e) => _calendar.NewAtAnchor();

        private void ImportBtn_Click(object sender, RoutedEventArgs e) => _ics.Import();

        private void ExportBtn_Click(object sender, RoutedEventArgs e) => _ics.Export();
    }
}
