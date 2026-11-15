using System;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>An empty day cell or hour slot opens a family card named for its day. An hour
    /// slot adds its time under the day; month cells have no hours, so theirs leaves it out.</summary>
    [Collection("Calendar settings")]
    public sealed class DayCardTests
    {
        [Fact]
        public void TheSlotCardIsNamedForItsDayAndAddsAnAppointmentThere() => OnSta(() =>
        {
            var at = new DateTime(2026, 11, 18, 14, 30, 0);
            DateTime? added = null;
            var card = CalendarChrome.DayMenu(at, d => added = d, timed: true);

            Assert.Equal("CardMenu", ResourceKeyOf(card));
            Assert.Equal(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(at.DayOfWeek),
                AutomationProperties.GetName((MenuItem)card.Items[0]));
            var meta = (TextBlock)((StackPanel)((MenuItem)card.Items[0]).Header).Children[1];
            Assert.Contains(Services.DateFormatManager.Time(at), meta.Text);
            var add = (MenuItem)card.Items[1];
            Assert.Equal("N", add.InputGestureText);
            add.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(at, added);
        });

        [Fact]
        public void AMonthCellCardShowsNoTime() => OnSta(() =>
        {
            var at = new DateTime(2026, 11, 18, 9, 0, 0);
            var card = CalendarChrome.DayMenu(at, _ => { });
            var meta = (TextBlock)((StackPanel)((MenuItem)card.Items[0]).Header).Children[1];
            Assert.Equal(Services.DateFormatManager.Format(at), meta.Text);
        });

        // CardBuilder.Menu sets the style by resource reference; with no Controls.xaml merged
        // here the reference is the evidence.
        private static string? ResourceKeyOf(FrameworkElement element) =>
            (element.ReadLocalValue(FrameworkElement.StyleProperty) as System.Windows.Expression)?.GetType()
                .GetProperty("ResourceKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                ?.GetValue(element.ReadLocalValue(FrameworkElement.StyleProperty)) as string;

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null) new Application();
                    body();
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The day card test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
