using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Killendar.Controls;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class DatePickerKeyboardRegressionTests
    {
        [Fact]
        public void DayControlExposesItsDateAndCanBeSelectedThroughNativeAutomation()
        {
            DatePickerBoundsRegressionTests.RunSta(() =>
            {
                var target = DatePickerBoundsRegressionTests.CreateTarget(new DateTime(2026, 1, 20));
                var menu = DatePickerFlyout.Create(target, target);
                var root = (StackPanel)((MenuItem)menu.Items[0]).Header;
                var days = (Grid)root.Children[1];
                var day = Assert.Single(days.Children.OfType<ToggleButton>(), button =>
                    AutomationProperties.GetName(button) == "Wednesday, January 21, 2026");
                Assert.True(day.Focusable);
                var peer = UIElementAutomationPeer.CreatePeerForElement(day);
                var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
                var selectedPeer = UIElementAutomationPeer.CreatePeerForElement(Day(menu, new DateTime(2026, 1, 20)));
                Assert.Equal(ToggleState.On, ((IToggleProvider)selectedPeer.GetPattern(PatternInterface.Toggle)).ToggleState);

                toggle.Toggle();

                Assert.Equal(DateFormatManager.Format(new DateTime(2026, 1, 21)), target.Text);
                Assert.False(menu.IsOpen);
            });
        }

        [Fact]
        public void ArrowKeyCrossesTheYearAndEnterConfirmsTheNewDay()
        {
            DatePickerBoundsRegressionTests.RunSta(() =>
            {
                using var source = new HwndSource(new HwndSourceParameters("Killendar picker key test")
                    { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
                var target = DatePickerBoundsRegressionTests.CreateTarget(new DateTime(2026, 12, 31));
                var menu = DatePickerFlyout.Create(target, target);
                var day = Day(menu, new DateTime(2026, 12, 31));
                var next = Key(day, source, System.Windows.Input.Key.Right);

                Assert.True(next.Handled);
                Assert.Equal("January 2027", MonthTitle(menu));
                Assert.Equal(DateFormatManager.Format(new DateTime(2026, 12, 31)), target.Text);
                var confirm = Key(Day(menu, new DateTime(2027, 1, 1)), source, System.Windows.Input.Key.Enter);
                Assert.True(confirm.Handled);
                Assert.Equal(DateFormatManager.Format(new DateTime(2027, 1, 1)), target.Text);
            });
        }

        [Fact]
        public void EscapeCancelsWithoutChangingTheDateField()
        {
            DatePickerBoundsRegressionTests.RunSta(() =>
            {
                using var source = new HwndSource(new HwndSourceParameters("Killendar picker cancel test")
                    { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
                var target = DatePickerBoundsRegressionTests.CreateTarget(new DateTime(2026, 1, 20));
                var menu = DatePickerFlyout.Create(target, target);

                var cancel = Key(Day(menu, new DateTime(2026, 1, 21)), source, System.Windows.Input.Key.Escape);

                Assert.True(cancel.Handled);
                Assert.Equal(DateFormatManager.Format(new DateTime(2026, 1, 20)), target.Text);
                Assert.False(menu.IsOpen);
            });
        }

        [Fact]
        public void MonthNavigationHasLocalizedNamesAndNativeInvocation()
        {
            DatePickerBoundsRegressionTests.RunSta(() =>
            {
                var target = DatePickerBoundsRegressionTests.CreateTarget(new DateTime(2026, 1, 20));
                var menu = DatePickerFlyout.Create(target, target);
                var header = (Grid)((StackPanel)((MenuItem)menu.Items[0]).Header).Children[0];
                var next = Assert.Single(header.Children.OfType<Button>(), button => AutomationProperties.GetName(button) == "Next");
                var invoke = Assert.IsAssignableFrom<IInvokeProvider>(UIElementAutomationPeer.CreatePeerForElement(next).GetPattern(PatternInterface.Invoke));

                invoke.Invoke();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

                Assert.Equal("February 2026", MonthTitle(menu));
                Assert.Equal(DateFormatManager.Format(new DateTime(2026, 1, 20)), target.Text);
            });
        }

        [Fact]
        public void MonthPagingAndSpaceSelectionWorkWithoutAMouse()
        {
            DatePickerBoundsRegressionTests.RunSta(() =>
            {
                using var source = new HwndSource(new HwndSourceParameters("Killendar picker month test")
                    { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
                var target = DatePickerBoundsRegressionTests.CreateTarget(new DateTime(2026, 1, 31));
                var menu = DatePickerFlyout.Create(target, target);

                Assert.True(Key(Day(menu, new DateTime(2026, 1, 31)), source, System.Windows.Input.Key.PageDown).Handled);
                Assert.Equal("February 2026", MonthTitle(menu));
                Assert.True(Key(Day(menu, new DateTime(2026, 2, 28)), source, System.Windows.Input.Key.Space).Handled);
                Assert.Equal(DateFormatManager.Format(new DateTime(2026, 2, 28)), target.Text);
            });
        }

        [Theory]
        [InlineData(1, 1, 1, System.Windows.Input.Key.PageUp)]
        [InlineData(1, 1, 1, System.Windows.Input.Key.Left)]
        [InlineData(9999, 12, 31, System.Windows.Input.Key.PageDown)]
        [InlineData(9999, 12, 31, System.Windows.Input.Key.Right)]
        public void KeyboardNavigationStopsAtSupportedDateBounds(int year, int month, int day, System.Windows.Input.Key key)
        {
            DatePickerBoundsRegressionTests.RunSta(() =>
            {
                using var source = new HwndSource(new HwndSourceParameters("Killendar picker bound test")
                    { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
                var date = new DateTime(year, month, day);
                var target = DatePickerBoundsRegressionTests.CreateTarget(date);
                var menu = DatePickerFlyout.Create(target, target);
                string title = MonthTitle(menu);

                Assert.True(Key(Day(menu, date), source, key).Handled);

                Assert.Equal(title, MonthTitle(menu));
                Assert.Equal(DateFormatManager.Format(date), target.Text);
            });
        }

        private static ToggleButton Day(ContextMenu menu, DateTime date)
            => Assert.Single(((Grid)((StackPanel)((MenuItem)menu.Items[0]).Header).Children[1])
                .Children.OfType<ToggleButton>(), button => Equals(button.Tag, date));

        private static string MonthTitle(ContextMenu menu)
            => ((TextBlock)((Grid)((StackPanel)((MenuItem)menu.Items[0]).Header).Children[0]).Children[1]).Text;

        private static KeyEventArgs Key(UIElement element, HwndSource source, System.Windows.Input.Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            element.RaiseEvent(args);
            return args;
        }
    }
}
