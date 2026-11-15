using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GM-005 and GM-006: clicks that dialogs mishandled.</summary>
    [Collection("Calendar settings")]
    public sealed class DialogClickFixTests
    {
        [Fact]
        public void ADoubleClickOnTheScrollBarIsNotARowChoice() => KitTestHost.Sta(() =>
        {
            var dialog = typeof(Killendar.Controls.SearchDialog);
            Assert.False((bool)KitTestHost.CallStatic(dialog, "IsOnRow", new System.Windows.Controls.Primitives.ScrollBar())!);
            Assert.False((bool)KitTestHost.CallStatic(dialog, "IsOnRow", new ListBox())!);
            Assert.False((bool)KitTestHost.CallStatic(dialog, "IsOnRow", new object[] { null! })!);
            Assert.True((bool)KitTestHost.CallStatic(dialog, "IsOnRow", new ListBoxItem())!);
        });

        [Fact]
        public void ACommitAfterALostFocusWaitsForTheClickToFinish() => KitTestHost.Sta(() =>
        {
            var window = new Window();
            int ran = 0;
            var guard = typeof(Killendar.Controls.SearchDialog).Assembly.GetType("Killendar.Controls.ClickGuard");
            Assert.True(guard != null, "ClickGuard does not exist.");

            // The button is still down: nothing may rebuild the rows yet.
            KitTestHost.CallStatic(guard!, "RunAfterClick", window, (System.Action)(() => ran++), (System.Func<bool>)(() => true));
            Flush();
            Assert.Equal(0, ran);

            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
            });
            Flush();
            Assert.Equal(1, ran);

            // With no button down the work still waits for the current input to finish, but runs.
            KitTestHost.CallStatic(guard!, "RunAfterClick", window, (System.Action)(() => ran++), (System.Func<bool>)(() => false));
            Flush();
            Assert.Equal(2, ran);
        });

        private static void Flush() =>
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new System.Action(() => { }));
    }
}
