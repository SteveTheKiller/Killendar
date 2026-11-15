using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Killendar.Controls
{
    /// <summary>
    /// Runs work that rebuilds a dialog's rows after the click that caused it has finished. A text
    /// box that commits on LostFocus loses focus on the mouse-down of the very click that is
    /// moving the focus; rebuilding the rows right then replaces the element under the pointer,
    /// and the click never lands.
    /// </summary>
    internal static class ClickGuard
    {
        internal static void RunAfterClick(UIElement owner, Action work, Func<bool>? buttonDown = null)
        {
            buttonDown ??= () => Mouse.LeftButton == MouseButtonState.Pressed;
            if (!buttonDown())
            {
                owner.Dispatcher.BeginInvoke(DispatcherPriority.Background, work);
                return;
            }

            MouseButtonEventHandler? up = null;
            up = (_, _) =>
            {
                owner.PreviewMouseLeftButtonUp -= up;
                owner.Dispatcher.BeginInvoke(DispatcherPriority.Background, work);
            };
            owner.PreviewMouseLeftButtonUp += up;
        }
    }
}
