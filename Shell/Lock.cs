using System.Linq;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using Killendar.Controls;
using Killendar.Features;
using Killendar.Services;

// Locking an encrypted Killendar: Ctrl+L or Lock now on the lock button's menu, after a chosen
// idle time, or when Windows locks. The controller closes the calendar (SecurityController.Lock);
// this side hides what was on screen and asks for the password once someone can see the window.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private DispatcherTimer? _idleTimer;
        private bool _unlockAsking;
        /// <summary>A lock that came while an import was writing; taken once it finishes.</summary>
        private bool _lockWaiting;

        /// <summary>Hidden, not collapsed: the layout holds still and the panel keeps its draft.</summary>
        void ISecurityHost.ShowLocked(bool locked)
        {
            ScaleHost.Visibility = locked ? Visibility.Hidden : Visibility.Visible;
            // Other Killendars shown alongside lock with the app; encrypted ones ask again.
            if (locked) ShownCalendars.CloseAll();
            else ShownCalendars.Reload(EventStore.ActiveFile);
        }

        private void AttachAutoLock()
        {
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _idleTimer.Tick += (_, _) =>
            {
                if (_lockWaiting && !_store.IsImporting) { LockAndAsk(); return; }
                if (_store.HasPassword && !_security.IsLocked &&
                    AutoLock.IdleDue(AutoLock.IdleTime(), AutoLock.IdleMinutes(Settings.Get(AutoLock.IdleSetting))))
                    LockAndAsk();
            };
            _idleTimer.Start();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            // SessionSwitch is static and would keep this window alive after it closes.
            Closed += (_, _) =>
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                _idleTimer.Stop();
            };
            // A window locked while hidden or minimized asks when it comes back.
            Activated += (_, _) => AskToUnlockWhenSeen();
            // Restoring from the taskbar can activate before the state leaves Minimized.
            StateChanged += (_, _) => AskToUnlockWhenSeen();
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock && Settings.Get(AutoLock.WindowsSetting) == "1")
                Dispatcher.BeginInvoke(new Action(() => { if (_store.HasPassword) LockAndAsk(); }));
        }

        private void LockAndAsk()
        {
            // An import writing on a worker thread owns the connection: lock when it is done.
            _lockWaiting = _store.IsImporting;
            if (_lockWaiting || !_store.HasPassword) return;
            // Overlays, menus and dialogs (search results above all) could still show the calendar.
            if (ShortcutsOverlay.Visibility == Visibility.Visible) ToggleShortcutsOverlay();
            if (_deletedMenu != null) _deletedMenu.IsOpen = false;
            foreach (Window owned in OwnedWindows.Cast<Window>().ToList()) owned.Close();
            var pending = _store.HasPassword ? _store.DueReminders(DateTime.Now).Count : 0;
            var coming = _store.HasPassword ? _store.UpcomingReminderTimes(DateTime.Now, TimeSpan.FromDays(1)) : [];
            if (!_security.Lock()) return;
            RemindWhileLocked(pending, coming);   // ReminderAlerts.cs
            Dispatcher.BeginInvoke(new Action(AskToUnlockWhenSeen), DispatcherPriority.Background);
        }

        private void AskToUnlockWhenSeen()
        {
            if (!_security.IsLocked || _unlockAsking || !IsVisible || WindowState == WindowState.Minimized) return;
            _unlockAsking = true;
            try { _security.Unlock(); }
            finally { _unlockAsking = false; }
            if (!_security.IsLocked) CheckReminders();
        }

        private void LockShortcut()
        {
            if (!_store.HasPassword) { StatusText.Text = Loc("Str_Status_LockNeedsPassword"); return; }
            LockAndAsk();
        }

        /// <summary>With a password, the lock button opens Lock now, the password change and the
        /// automatic locks; without one it goes straight to setting a password, as before.</summary>
        private void ShowLockMenu()
        {
            var menu = new ContextMenu();
            FlyoutPlacement.AttachTopRight(menu);   // the pane's top-right corner, 8px in
            var lockNow = new MenuItem { Header = Loc("Str_Lock_Now"), InputGestureText = "Ctrl+L" };
            lockNow.Click += (_, _) => LockAndAsk();
            var change = new MenuItem { Header = Loc("Str_Lock_Change") };
            change.Click += (_, _) => _security.ToggleLock();

            var idle = new MenuItem { Header = Loc("Str_Lock_Idle") };
            int current = AutoLock.IdleMinutes(Settings.Get(AutoLock.IdleSetting));
            foreach (int minutes in new[] { 0 }.Concat(AutoLock.IdleChoices))
            {
                var choice = new MenuItem
                {
                    Header = minutes == 0 ? Loc("Str_Lock_IdleNever") : string.Format(Loc("Str_Lock_IdleMinutes"), minutes),
                    IsCheckable = true,
                    IsChecked = minutes == current,
                };
                choice.Click += (_, _) => Settings.Set(AutoLock.IdleSetting, minutes.ToString());
                idle.Items.Add(choice);
            }
            var withWindows = new MenuItem
            {
                Header = Loc("Str_Lock_WithWindows"),
                IsCheckable = true,
                IsChecked = Settings.Get(AutoLock.WindowsSetting) == "1",
            };
            withWindows.Click += (_, _) => Settings.Set(AutoLock.WindowsSetting, withWindows.IsChecked ? "1" : "0");

            menu.Items.Add(lockNow);
            menu.Items.Add(change);
            menu.Items.Add(new Separator());
            menu.Items.Add(idle);
            menu.Items.Add(withWindows);
            menu.IsOpen = true;
        }
    }
}
