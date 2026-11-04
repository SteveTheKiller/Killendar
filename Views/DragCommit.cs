using System;
using System.Windows;
using System.Windows.Input;

namespace Killendar.Views
{
    /// <summary>
    /// What Month and the time grids share about ending a chip drag. A release commits only over
    /// the calendar surface: let go over the sidebar, the toolbar, the day headers or the time
    /// gutter and the chip snaps home, where it used to land on the nearest edge cell. Escape
    /// cancels the drag in flight the same way.
    /// </summary>
    internal static class DragCommit
    {
        /// <summary>True when the pointer is over the drop surface. Half-open on the right and
        /// bottom, so the pixel past the last column or row is already off the grid.</summary>
        internal static bool Lands(Point pointer, Rect surface) =>
            !surface.IsEmpty && surface.Width > 0 && surface.Height > 0 &&
            pointer.X >= surface.Left && pointer.X < surface.Right &&
            pointer.Y >= surface.Top && pointer.Y < surface.Bottom;

        // The drag in flight, as the call that cancels it. One at a time: there is one mouse.
        private static Action? _cancel;
        private static Window? _window;

        /// <summary>A drag has started; <paramref name="cancel"/> snaps it home without a commit.</summary>
        internal static void Begin(Action cancel) => _cancel = cancel;

        /// <summary>The drag ended, dropped or cancelled. A stale end from another chip is ignored.</summary>
        internal static void End(Action cancel)
        {
            if (_cancel != null && _cancel.Equals(cancel)) _cancel = null;
        }

        /// <summary>Cancels the drag in flight, if there is one. True when it did.</summary>
        internal static bool TryCancel()
        {
            var cancel = _cancel;
            if (cancel == null) return false;
            _cancel = null;
            cancel();
            return true;
        }

        /// <summary>
        /// Starts a chip's drag: registers its cancel and listens for Escape on the chip's window.
        /// Keyboard input goes to the focused element, not the one holding mouse capture, so the
        /// chip itself would never hear the key. Releasing capture runs the chip's own reset,
        /// which commits nothing.
        /// </summary>
        internal static void BeginFor(UIElement chip)
        {
            Begin(chip.ReleaseMouseCapture);
            Listen(Window.GetWindow(chip));
        }

        internal static void EndFor(UIElement chip)
        {
            End(chip.ReleaseMouseCapture);
            if (_cancel == null) Listen(null);
        }

        private static void Listen(Window? window)
        {
            if (_window == window) return;
            _window?.RemoveHandler(Keyboard.PreviewKeyDownEvent, (KeyEventHandler)OnPreviewKeyDown);
            _window = window;
            _window?.AddHandler(Keyboard.PreviewKeyDownEvent, (KeyEventHandler)OnPreviewKeyDown, handledEventsToo: true);
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && TryCancel()) e.Handled = true;
        }
    }
}
