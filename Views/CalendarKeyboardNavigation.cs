using System.Windows;
using System.Windows.Input;

namespace Killendar.Views
{
    internal static class CalendarKeyboardNavigation
    {
        internal static readonly DependencyProperty IsAppointmentProperty = DependencyProperty.RegisterAttached(
            "IsAppointment", typeof(bool), typeof(CalendarKeyboardNavigation), new PropertyMetadata(false));

        internal static void Attach(FrameworkElement view)
        {
            KeyboardNavigation.SetTabNavigation(view, KeyboardNavigationMode.Once);
            KeyboardNavigation.SetDirectionalNavigation(view, KeyboardNavigationMode.Contained);
        }

        internal static bool HandleKey(KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None || Keyboard.FocusedElement is not FrameworkElement appointment ||
                !(bool)appointment.GetValue(IsAppointmentProperty)) return false;
            FocusNavigationDirection direction;
            switch (e.Key)
            {
                case Key.Left: direction = FocusNavigationDirection.Left; break;
                case Key.Right: direction = FocusNavigationDirection.Right; break;
                case Key.Up: direction = FocusNavigationDirection.Up; break;
                case Key.Down: direction = FocusNavigationDirection.Down; break;
                default: return false;
            }
            appointment.MoveFocus(new TraversalRequest(direction));
            // At the edge, keep focus in the view instead of changing its displayed period.
            e.Handled = true;
            return true;
        }
    }
}
