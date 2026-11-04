using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A Month day opens on a click anywhere in its square. The day's event list fills
    /// everything below the date number, and a plain ScrollViewer marks every click on it handled
    /// when it takes focus, so only the strip with the number used to open the day.</summary>
    public sealed class MonthDayClickTests
    {
        [Fact]
        public void AClickInTheEventAreaReachesTheDay()
        {
            var (reached, focused) = ClickInside(() => new DayScrollViewer());
            Assert.True(reached, "the click stopped at the day's event list");
            Assert.True(focused, "the event list no longer takes focus on a click");
        }

        [Fact]
        public void APlainScrollViewerSwallowsTheClick()
        {
            // The reason DayScrollViewer exists. If this ever passes through, the test above has
            // stopped telling the two apart.
            Assert.False(ClickInside(() => new ScrollViewer()).Reached);
        }

        private static (bool Reached, bool Focused) ClickInside(Func<ScrollViewer> make)
        {
            Exception? failure = null;
            bool reached = false, focused = false;
            var thread = new Thread(() =>
            {
                try
                {
                    var scroll = make();
                    scroll.Content = new StackPanel();
                    var cell = new Border { Child = scroll };
                    cell.MouseLeftButtonDown += (_, _) => reached = true;
                    // A message-only window (parent HWND_MESSAGE) gives the cell a real presentation
                    // source, so focus behaves as it does in the app, without anything on screen.
                    using var source = new System.Windows.Interop.HwndSource(
                        new System.Windows.Interop.HwndSourceParameters("MonthDayClick") { ParentWindow = new IntPtr(-3), WindowStyle = 0 })
                    { RootVisual = cell };
                    cell.Measure(new Size(50, 50));
                    cell.Arrange(new Rect(0, 0, 50, 50));
                    // A real click raises the bubbling MouseDown, which WPF turns into the direct
                    // MouseLeftButtonDown on each element along the route.
                    scroll.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = Mouse.MouseDownEvent,
                    });
                    focused = scroll.IsKeyboardFocused;
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);
            return (reached, focused);
        }
    }
}
