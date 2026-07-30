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
                if (AboutOverlay.Visibility == Visibility.Visible) { AboutClose_Click(this, new RoutedEventArgs()); }
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
                case Key.F1:     ShowAboutOverlay(); break;

                default: return;   // leave e.Handled alone so anything else still routes normally
            }

            e.Handled = true;
        }


    }
}
