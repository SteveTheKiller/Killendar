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
        /// <summary>True when a text field has focus, so a bare letter belongs to the field.</summary>
        private static bool TypingInField()
            => Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            bool ctrlShortcut = ctrl && !alt;

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

            // ---- modified: safe while typing ----
            if (ctrlShortcut)
            {
                switch (e.Key)
                {
                    case Key.I: _ics.Import();          e.Handled = true; return;
                    case Key.E: _ics.Export();          e.Handled = true; return;
                    case Key.N: _calendar.NewAtAnchor(); e.Handled = true; return;
                }
                return;
            }

            // Escape closes whatever is on top, and is allowed even mid-typing: bailing out of a
            // half-filled form is exactly when you press it.
            if (e.Key == Key.Escape)
            {
                if (ShortcutsOverlay.Visibility == Visibility.Visible) { FadeOverlayOut(ShortcutsOverlay); }
                else if (AboutOverlay.Visibility == Visibility.Visible) { AboutClose_Click(this, new RoutedEventArgs()); }
                else if (_sidebarOpen) { CloseSidebar(); }
                e.Handled = true;
                return;
            }

            if (TypingInField()) return;

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
