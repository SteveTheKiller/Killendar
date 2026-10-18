using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Models;
using Killendar.Services;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class CalendarKeyboardTests
    {
        [Fact]
        public void EveryGridGroupsAppointmentsWithoutGroupingHeaderActions()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null) new Application();
                    var day = new DateTime(2026, 10, 14);
                    var store = new EventStore();
                    store.Add(new CalendarEvent { Title = "Timed", Start = day.AddHours(9), End = day.AddHours(10) });
                    store.Add(new CalendarEvent { Title = "All day", Start = day, End = day.AddDays(1), AllDay = true });
                    foreach (ICalendarView view in new ICalendarView[] { new MonthView(), new WeekView(), new DayView() })
                    {
                        view.Initialize(store); view.Anchor = day;
                        var control = (FrameworkElement)view;
                        control.Measure(new Size(900, 600)); control.Arrange(new Rect(0, 0, 900, 600)); control.UpdateLayout();
                        var descendants = Descendants(control).OfType<FrameworkElement>().ToArray();
                        var group = Assert.Single(descendants, e => KeyboardNavigation.GetTabNavigation(e) == KeyboardNavigationMode.Once);
                        var chips = descendants.OfType<CalendarChrome.ChipBorder>().ToArray();
                        Assert.NotEmpty(chips);
                        foreach (var chip in chips)
                        {
                            Assert.True((bool)chip.GetValue(CalendarKeyboardNavigation.IsAppointmentProperty));
                            Assert.True(group.IsAncestorOf(chip));
                        }
                        Assert.All(descendants.OfType<ScrollViewer>(), scroller => Assert.False(scroller.Focusable));
                        Assert.All(descendants.OfType<Button>(), button => Assert.False(group.IsAncestorOf(button)));
                    }
                }
                catch (Exception error) { failure = error; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
