using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GL-008: a reminder row opens on a full click or the keyboard, never on a stray release.</summary>
    [Collection("Calendar settings")]
    public sealed class PressActivationTests
    {
        private static Type Helper() => typeof(App).Assembly.GetType("Killendar.Controls.PressActivation")!;

        private static MouseButtonEventArgs Mouse_(RoutedEvent routed) =>
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routed };

        [Fact]
        public void AReleaseWithoutAPressDoesNothing() => KitTestHost.Sta(() =>
        {
            int opened = 0;
            var panel = new StackPanel();
            KitTestHost.CallStatic(Helper(), "Attach", panel, (Action)(() => opened++));
            panel.RaiseEvent(Mouse_(UIElement.MouseLeftButtonUpEvent));
            Assert.Equal(0, opened);

            panel.RaiseEvent(Mouse_(UIElement.MouseLeftButtonDownEvent));
            panel.RaiseEvent(Mouse_(UIElement.MouseLeftButtonUpEvent));
            Assert.Equal(1, opened);

            // The press is spent: a second release alone does not open it again.
            panel.RaiseEvent(Mouse_(UIElement.MouseLeftButtonUpEvent));
            Assert.Equal(1, opened);
        });

        [Fact]
        public void APressThatLeavesTheRowBeforeTheReleaseIsCanceled() => KitTestHost.Sta(() =>
        {
            int opened = 0;
            var panel = new StackPanel();
            KitTestHost.CallStatic(Helper(), "Attach", panel, (Action)(() => opened++));
            panel.RaiseEvent(Mouse_(UIElement.MouseLeftButtonDownEvent));
            panel.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
            panel.RaiseEvent(Mouse_(UIElement.MouseLeftButtonUpEvent));
            Assert.Equal(0, opened);
        });

        [Fact]
        public void EnterAndSpaceOpenTheRowAndItCanTakeFocus() => KitTestHost.Sta(() =>
        {
            int opened = 0;
            var panel = new StackPanel();
            KitTestHost.CallStatic(Helper(), "Attach", panel, (Action)(() => opened++));
            Assert.True(panel.Focusable);
            Assert.True(KeyboardNavigation.GetIsTabStop(panel));

            using var source = new HwndSource(new HwndSourceParameters("press-test") { ParentWindow = new IntPtr(-3) });
            foreach (var key in new[] { Key.Enter, Key.Space, Key.A })
                panel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = UIElement.KeyDownEvent });
            Assert.Equal(2, opened);
        });
    }
}
