using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows.Threading;
using Killendar.Controls;
using Killendar.Services;

// Reminders come due while Killendar runs, the window in the tray included; the check also runs
// once right after the calendar opens, so what came due while it was closed shows then. A locked
// calendar cannot be read, so it shows only how many came due, from times taken just before the
// lock (Lock.cs).
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private DispatcherTimer? _reminderTimer;
        private RemindersWindow? _remindersWindow;
        private HashSet<(Guid, DateTime, DateTime)> _shownReminders = [];
        private int _lockedPending;
        private List<DateTime> _lockedComing = [];
        private int _lockedShown;

        private void StartReminders()
        {
            if (_reminderTimer == null)
            {
                _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                _reminderTimer.Tick += (_, _) => CheckReminders();
                _reminderTimer.Start();
                Closed += (_, _) =>
                {
                    _reminderTimer.Stop();
                    _remindersWindow?.Close();
                };
            }
            CheckReminders();
        }

        private void CheckReminders()
        {
            if (_reminderTimer == null) return;   // not started: the first open has not finished
            if (_store.IsImporting) return;       // the worker owns the connection; next tick
            if (_security.IsLocked)
            {
                int count = _lockedPending + _lockedComing.Count(t => t <= DateTime.Now);
                if (count > _lockedShown)
                {
                    // A closed window stays closed until another reminder comes due.
                    _lockedShown = count;
                    ReminderWindow().ShowLockedCount(count);
                    Alert();
                    _tray.Notify(AppInfo.DisplayName, string.Format(Loc("Str_Rem_Locked"), count));
                }
                return;
            }
            if (!_store.IsOpen) return;
            var due = _store.DueReminders(DateTime.Now);
            if (due.Count == 0)
            {
                _remindersWindow?.Close();
                _shownReminders.Clear();
                return;
            }
            // Fire is part of the key so a snoozed reminder counts as new when it comes back.
            var keys = new HashSet<(Guid, DateTime, DateTime)>(due.Select(d => (d.Id, d.Occurrence, d.Fire)));
            bool fresh = !keys.IsSubsetOf(_shownReminders);
            _shownReminders = keys;
            if (_remindersWindow == null && !fresh) return;   // closed by the user; wait for a new one
            ReminderWindow().ShowReminders(due, Describe);
            if (fresh)
            {
                Alert();
                // In the tray the reminders window still shows; the balloon says where it came from.
                var first = due[0];
                string title = string.IsNullOrWhiteSpace(first.Title) ? Loc("Str_Cal_NoTitle") : first.Title;
                string text = Describe(first) + (due.Count > 1 ? Environment.NewLine + string.Format(Loc("Str_Rem_More"), due.Count - 1) : "");
                _tray.Notify(title, text);
            }
        }

        private void RemindWhileLocked(int pending, List<DateTime> coming)
        {
            _lockedPending = pending;
            _lockedComing = coming;
            _lockedShown = 0;
            _shownReminders.Clear();
            _remindersWindow?.Close();
            CheckReminders();
        }

        private RemindersWindow ReminderWindow()
        {
            if (_remindersWindow != null) return _remindersWindow;
            var window = new RemindersWindow();
            window.Dismissed += reminder => { _store.DismissReminder(reminder); CheckReminders(); };
            window.DismissedAll += reminders =>
            {
                foreach (var reminder in reminders) _store.DismissReminder(reminder);
                CheckReminders();
            };
            window.Snoozed += (reminder, minutes) =>
            {
                _store.SnoozeReminder(reminder, DateTime.Now.AddMinutes(minutes));
                CheckReminders();
            };
            window.OpenRequested += reminder =>
            {
                ShowFromTray();
                Activate();
                if (reminder != null && !_security.IsLocked) ShowDayAgenda(reminder.Start.Date, reminder.Id);
            };
            window.Closed += (_, _) => { if (_remindersWindow == window) _remindersWindow = null; };
            _remindersWindow = window;
            window.Show();
            return window;
        }

        private static void Alert()
        {
            try { SystemSounds.Asterisk.Play(); } catch { /* no sound device */ }
        }

        private string Describe(DueReminder reminder)
        {
            string when = reminder.AllDay
                ? DateFormatManager.MonthDay(reminder.Start)
                : DateFormatManager.MonthDay(reminder.Start) + " " + DateFormatManager.Time(reminder.Start)
                  + " - " + DateFormatManager.Time(reminder.End);
            return string.IsNullOrWhiteSpace(reminder.Location) ? when : when + "    " + reminder.Location;
        }
    }
}
