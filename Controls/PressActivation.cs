using System;
using System.Windows;
using System.Windows.Input;

namespace Killendar.Controls
{
    /// <summary>
    /// Makes a plain element act like a button: it activates on a full click (press, then release
    /// on the same element) and on Enter or Space, and it can take keyboard focus. Activating on
    /// the release alone would fire for a drag that merely ended over the element.
    /// </summary>
    internal static class PressActivation
    {
        internal static void Attach(UIElement element, Action activate)
        {
            bool pressed = false;
            element.Focusable = true;
            KeyboardNavigation.SetIsTabStop(element, true);
            element.MouseLeftButtonDown += (_, _) => pressed = true;
            element.MouseLeave += (_, _) => pressed = false;
            element.MouseLeftButtonUp += (_, _) =>
            {
                if (!pressed) return;
                pressed = false;
                activate();
            };
            element.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                activate();
            };
        }
    }
}
