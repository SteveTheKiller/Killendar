using System;
using System.Windows;
using System.Windows.Input;

namespace Killendar.Controls
{
    /// <summary>Title-bar dragging that survives a touch tap or fast click: DragMove throws when
    /// the button is already up by the time it runs. Every dialog's title-bar press comes here.</summary>
    internal static class TitleBarDrag
    {
        internal static bool ShouldDrag(MouseButtonState state) => state == MouseButtonState.Pressed;

        internal static void Start(Window window, MouseButtonEventArgs e)
        {
            if (!ShouldDrag(e.ButtonState)) return;
            try { window.DragMove(); }
            catch (InvalidOperationException) { }   // the button was already up
        }
    }
}
