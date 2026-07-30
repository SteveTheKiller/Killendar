using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

// The Killendar - keyboard shortcuts. Partial of MainWindow.
//
// Single keys, no modifier, the way the rest of the family does it. The one rule that makes that
// safe: a single-key shortcut must never fire while the user is typing, so everything bails out
// when focus is in a text field. Modified combinations (Ctrl+I, Ctrl+E) are checked first and are
// allowed regardless, since they cannot collide with typing.
namespace Killendar
{
    public partial class MainWindow
    {
        /// <summary>True when a text field has focus, so a bare letter belongs to the field.</summary>
        private static bool TypingInField()
            => Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            // ---- modified: safe while typing ----
            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.I: ImportBtn_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.E: ExportBtn_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.N: OpenNewAppointment(NewAppointmentDefault()); e.Handled = true; return;
                }
                return;
            }

            // Escape closes whatever is on top, and is allowed even mid-typing: bailing out of a
            // half-filled form is exactly when you press it.
            if (e.Key == Key.Escape)
            {
                if (AboutOverlay.Visibility == Visibility.Visible) { AboutClose_Click(this, new RoutedEventArgs()); }
                else if (_sidebarOpen) { CloseSidebar(); }
                e.Handled = true;
                return;
            }

            if (TypingInField()) return;

            switch (e.Key)
            {
                case Key.N:      OpenNewAppointment(NewAppointmentDefault()); break;
                case Key.T:      TodayBtn_Click(this, new RoutedEventArgs()); break;

                case Key.Left:
                case Key.OemComma:  Move(-1); break;
                case Key.Right:
                case Key.OemPeriod: Move(1); break;

                case Key.M:
                case Key.D1: SelectView("Month");  break;
                case Key.W:
                case Key.D2: SelectView("Week");   break;
                case Key.D:
                case Key.D3: SelectView("Day");    break;
                case Key.A:
                case Key.D4: SelectView("Agenda"); break;

                case Key.B:      SidebarToggle_Click(this, new RoutedEventArgs()); break;
                case Key.F1:     ShowAboutOverlay(); break;

                default: return;   // leave e.Handled alone so anything else still routes normally
            }

            e.Handled = true;
        }

        /// <summary>Where a keyboard-started appointment lands: 9am on the day in view.</summary>
        private System.DateTime NewAppointmentDefault()
        {
            var day = _anchor.Date == System.DateTime.Today ? System.DateTime.Today : _anchor.Date;
            return day.AddHours(9);
        }
    }
}
