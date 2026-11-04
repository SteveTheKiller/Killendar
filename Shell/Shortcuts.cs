using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

// The Killendar - keyboard shortcuts. Partial of MainWindow.
//
// Single keys, no modifier. The one rule that makes that
// safe: a single-key shortcut must never fire while the user is typing, so everything bails out
// when focus is in a text field. Modified combinations (Ctrl+I, Ctrl+E) are checked first and are
// allowed regardless, since they cannot collide with typing.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void UndoRedo(bool undo)
        {
            bool done = undo ? _store.Undo() : _store.Redo();
            if (done && _agendaDay != null) BuildDayAgendaRows();
            StatusText.Text = _store.LoadError != null
                ? string.Format(Loc("Str_Status_UndoFailed"), _store.LoadError)
                : Loc(done ? (undo ? "Str_Status_Undone" : "Str_Status_Redone")
                           : (undo ? "Str_Status_NothingToUndo" : "Str_Status_NothingToRedo"));
        }

        /// <summary>True when a text field has focus, so a bare letter belongs to the field.</summary>
        private static bool TypingInField()
            => Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool ctrlShortcut = ctrl && !alt;

            // Locked: no shortcut reaches the calendar; a key press asks for the password (Lock.cs).
            if (_security.IsLocked)
            {
                // After a canceled prompt Tab, Space and Enter reach the title-bar buttons, and the
                // window menu keys are never swallowed (LockedKeyPolicy).
                if (LockedKeyPolicy.Decide(true, _unlockDeclined, e.Key, e.SystemKey, Keyboard.Modifiers)
                    == LockedKeyAction.AskToUnlock)
                {
                    e.Handled = true;
                    AskToUnlock();
                }
                return;
            }

            if (AboutOverlay.Visibility == Visibility.Visible || ShortcutsOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    if (AboutOverlay.Visibility == Visibility.Visible) AboutClose_Click(this, new RoutedEventArgs());
                    else FadeOverlayOut(ShortcutsOverlay);
                    e.Handled = true;
                    return;
                }
                if (!ctrl && !alt && !shift && e.Key == Key.F1)
                {
                    ToggleShortcutsOverlay();
                    e.Handled = true;
                    return;
                }
                if (!ctrl && !alt && !shift && e.Key == Key.F12)
                {
                    ShowAboutOverlay();
                    e.Handled = true;
                    return;
                }
                // Let the focused overlay control handle its own input. Film grain is the
                // existing global action that remains safe while a modal card is visible.
                if (!(alt && shift && !ctrl && e.SystemKey == Key.G)) return;
            }

            if (HandleLocationSuggestionKey(e)) return;

            // Appointment-editor accelerators remain available while a text box owns focus.
            // Ctrl+Enter is used instead of Ctrl+S so a multiline description keeps its normal
            // editing behavior; destructive delete retains the editor's confirmation path.
            bool editingAppointment = _sidebarOpen && EditorScroll.Visibility == Visibility.Visible;
            if (editingAppointment && ctrlShortcut && e.Key == Key.Enter)
            {
                SaveAppointment_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (editingAppointment && ctrlShortcut && e.Key == Key.Delete &&
                DeleteAppointmentBtn.Visibility == Visibility.Visible)
            {
                DeleteAppointment_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (editingAppointment && ctrlShortcut && !shift && e.Key == Key.D && DuplicateAppointmentBtn.Visibility == Visibility.Visible)
            {
                _appointments.Duplicate();
                e.Handled = true;
                return;
            }
            // Alt+arrows move the appointment being edited: 15 minutes up or down, a day left or
            // right - the keyboard's way to do what dragging does.
            if (editingAppointment && alt && !shift && !ctrl &&
                e.SystemKey is Key.Up or Key.Down or Key.Left or Key.Right)
            {
                _appointments.Nudge(e.SystemKey switch
                {
                    Key.Up => TimeSpan.FromMinutes(-15),
                    Key.Down => TimeSpan.FromMinutes(15),
                    Key.Left => TimeSpan.FromDays(-1),
                    _ => TimeSpan.FromDays(1),
                });
                e.Handled = true;
                return;
            }
            if (editingAppointment && alt && e.SystemKey == Key.A)
            {
                AllDayToggle.IsChecked = !AllDayToggle.IsChecked;
                AllDayToggle_Click(AllDayToggle, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (editingAppointment && alt && e.SystemKey == Key.R)
            {
                RepeatFreqButton_Click(RepeatFreqButton, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // Alt+Shift+G, the family Film Grain chord. Safe while typing: a text field takes no
            // Alt chords.
            if (alt && shift && !ctrl && e.SystemKey == Key.G)
            {
                if (!e.IsRepeat) _themePicker.ToggleFilmGrain();   // Controls/ThemePicker.cs
                e.Handled = true;
                return;
            }

            // Alt+Shift+Left or Right, the family keys for a movable pane: docks the appointment
            // panel to that side of the calendar, and the arrow of the side it is already on opens
            // or closes it. Safe while typing for the same reason. (SidebarDock.cs)
            if (alt && shift && !ctrl && (e.SystemKey == Key.Left || e.SystemKey == Key.Right))
            {
                bool right = e.SystemKey == Key.Right;
                if (!e.IsRepeat)
                {
                    if (right == _sidebarRight) SidebarToggle_Click(this, new RoutedEventArgs());
                    else SelectSidebarSide(right);
                }
                e.Handled = true;
                return;
            }

            if (ctrlShortcut && shift && e.Key == Key.M &&
                _calendar.ToggleFocusedMonthAppointment())
            {
                e.Handled = true;
                return;
            }

            // ---- modified: safe while typing ----
            if (ctrlShortcut)
            {
                switch (e.Key)
                {
                    case Key.I: _ics.Import();          e.Handled = true; return;
                    case Key.E: _ics.Export();          e.Handled = true; return;
                    case Key.N: _calendar.NewAtAnchor(); e.Handled = true; return;
                    case Key.L: LockShortcut();          e.Handled = true; return;   // Lock.cs
                    case Key.F: OpenSearch();            e.Handled = true; return;   // Search.cs
                    case Key.G: OpenSearch();            e.Handled = true; return;   // the same box takes a date
                    // A text box keeps its own Ctrl+Z, and an open editor holds a copy of the row
                    // an undo might rewrite, so calendar undo waits until neither is in the way.
                    case Key.Z when !TypingInField() && !editingAppointment: UndoRedo(undo: true);  e.Handled = true; return;
                    case Key.Y when !TypingInField() && !editingAppointment: UndoRedo(undo: false); e.Handled = true; return;
                }
                return;
            }

            // Escape closes whatever is on top, and is allowed even mid-typing: bailing out of a
            // half-filled form is exactly when you press it.
            if (e.Key == Key.Escape)
            {
                switch (ShortcutKeyPolicy.Escape(FieldReminder.IsDropDownOpen || FieldZone.IsDropDownOpen,
                    ShortcutsOverlay.Visibility == Visibility.Visible, AboutOverlay.Visibility == Visibility.Visible, _sidebarOpen))
                {
                    // Not handled, so the ComboBox gets the paired KeyDown and closes its own list.
                    case EscapeAction.LeaveToDropDown: return;
                    case EscapeAction.CloseShortcuts: FadeOverlayOut(ShortcutsOverlay); break;
                    case EscapeAction.CloseAbout: AboutClose_Click(this, new RoutedEventArgs()); break;
                    case EscapeAction.CloseSidebar: CloseSidebar(); SidebarToggleBtn.Focus(); break;
                }
                e.Handled = true;
                return;
            }

            // A focused dropdown or check box in the editor keeps its letters and arrows too.
            var focused = Keyboard.FocusedElement;
            if (ShortcutKeyPolicy.BareKeyBelongsToFocus(focused,
                    focused is DependencyObject d && EditorScroll.IsAncestorOf(d), e.Key))
                return;

            // A focused day or time slot uses the arrows to move between days and slots
            // (Views/CalendarKeyboard.cs); this preview handler runs first, so it must step aside
            // or Left/Right would page the calendar instead.
            if ((e.Key == Key.Left || e.Key == Key.Right) &&
                (Keyboard.FocusedElement is Views.CalendarKeyboard.DayBorder ||
                 Keyboard.FocusedElement is Views.CalendarKeyboard.SlotCanvas))
                return;

            switch (e.Key)
            {
                case Key.N:      _calendar.NewAtAnchor(); break;
                case Key.T:      _calendar.GoToday(); break;

                case Key.Left:
                case Key.OemComma:  _calendar.Move(-1); break;
                case Key.Right:
                case Key.OemPeriod: _calendar.Move(1); break;

                case Key.M:
                case Key.D1: _calendar.SelectView("Month");  break;
                case Key.W:
                case Key.D2: _calendar.SelectView("Week");   break;
                case Key.D:
                case Key.D3: _calendar.SelectView("Day");    break;
                case Key.A:
                case Key.D4: _calendar.SelectView("Agenda"); break;
                case Key.R:      ToggleMonthMode(); break;

                case Key.B:      SidebarToggle_Click(this, new RoutedEventArgs()); break;
                case Key.F5:     _security.ReloadActive(); break;

                // Family standard, and it is the same in every app: F1 is the shortcuts overlay,
                // F12 is About. Killendar had F1 on About until 2026-07-30, which was the odd one
                // out - do not swap these back.
                case Key.F1:     ToggleShortcutsOverlay(); break;
                case Key.F12:    ShowAboutOverlay(); break;

                default: return;   // leave e.Handled alone so anything else still routes normally
            }

            e.Handled = true;
        }


    }
}

namespace Killendar.Shell
{
    public enum LockedKeyAction { PassThrough, AskToUnlock }

    public enum EscapeAction { None, LeaveToDropDown, CloseShortcuts, CloseAbout, CloseSidebar }

    /// <summary>The decisions behind the window's key routing, kept free of controls so they can be
    /// tested without a window.</summary>
    internal static class ShortcutKeyPolicy
    {
        /// <summary>What Escape closes: whatever is on top. An overlay covers everything, and an
        /// open dropdown in the editor covers the panel it sits in.</summary>
        internal static EscapeAction Escape(bool dropDownOpen, bool shortcutsVisible, bool aboutVisible, bool sidebarOpen)
        {
            if (shortcutsVisible) return EscapeAction.CloseShortcuts;
            if (aboutVisible) return EscapeAction.CloseAbout;
            if (dropDownOpen) return EscapeAction.LeaveToDropDown;
            if (sidebarOpen) return EscapeAction.CloseSidebar;
            return EscapeAction.None;
        }

        /// <summary>True when a bare key belongs to the focused control rather than to a calendar
        /// shortcut. <paramref name="inEditor"/> says the control sits in the appointment editor.</summary>
        internal static bool BareKeyBelongsToFocus(object? focused, bool inEditor, Key key)
        {
            if (focused is TextBox || focused is PasswordBox) return true;
            // A dropdown searches its items by letter (an open list's items live in a popup, so
            // they are not inside the editor's tree); a check box or chip in the editor uses the
            // arrows to move between its neighbors.
            bool owner = focused is ComboBox || focused is ComboBoxItem ||
                         (inEditor && focused is System.Windows.Controls.Primitives.ToggleButton);
            return owner && (key is >= Key.A and <= Key.Z || key is >= Key.D0 and <= Key.D9 ||
                             key is Key.Left or Key.Right or Key.OemComma or Key.OemPeriod);
        }
    }

    internal static class LockedKeyPolicy
    {
        /// <summary>What a key press does while the window is locked. A declined prompt must not trap
        /// keyboard users: Tab, Shift+Tab, Space and Enter reach the title-bar buttons (and the modifier
        /// press that starts Shift+Tab), and the window menu keys Alt+F4 and Alt+Space are never taken.
        /// Anything else still asks for the password.</summary>
        internal static LockedKeyAction Decide(bool isLocked, bool declined, System.Windows.Input.Key key,
            System.Windows.Input.Key systemKey, System.Windows.Input.ModifierKeys modifiers)
        {
            if (!isLocked) return LockedKeyAction.PassThrough;
            var real = key == System.Windows.Input.Key.System ? systemKey : key;
            if (key == System.Windows.Input.Key.System &&
                (real == System.Windows.Input.Key.F4 || real == System.Windows.Input.Key.Space))
                return LockedKeyAction.PassThrough;
            if (!declined) return LockedKeyAction.AskToUnlock;
            switch (real)
            {
                case System.Windows.Input.Key.LeftShift: case System.Windows.Input.Key.RightShift:
                case System.Windows.Input.Key.LeftCtrl: case System.Windows.Input.Key.RightCtrl:
                case System.Windows.Input.Key.LeftAlt: case System.Windows.Input.Key.RightAlt:
                    return LockedKeyAction.PassThrough;
                case System.Windows.Input.Key.Tab: case System.Windows.Input.Key.Space: case System.Windows.Input.Key.Enter:
                    bool plain = (modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt)) == 0;
                    return plain ? LockedKeyAction.PassThrough : LockedKeyAction.AskToUnlock;
                default:
                    return LockedKeyAction.AskToUnlock;
            }
        }
    }
}
