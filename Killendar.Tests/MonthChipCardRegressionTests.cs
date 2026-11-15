using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// <summary>The month chip card's own rows act on the day the chip sits in, and changing one
    /// chip's size leaves the rest of the grid alone.</summary>
    [Collection("Calendar settings")]
    public sealed class MonthChipCardRegressionTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        // A bar from Monday November 23 to Wednesday December 2 crosses into the next grid week.
        private static readonly CalendarEvent Trip = new()
        {
            Title = "Trip",
            Start = new DateTime(2026, 11, 23, 9, 0, 0),
            End = new DateTime(2026, 12, 2, 17, 0, 0),
        };

        // UI-003: Add on the second week's bar added on the trip's first day.
        [Fact]
        public void AddOnASpanningBarAddsOnTheBarsOwnDay() => WithMonth((view, store) =>
        {
            var (bar, day) = SecondBar(view);
            DateTime? added = null;
            view.SlotSelected += d => added = d;
            var add = OpenCard(bar).Items.OfType<MenuItem>().Single(i => i.InputGestureText == "N");
            add.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(day.AddHours(9), added);
        });

        // UI-005: the Add row shows N, so N in the open card adds on that day.
        [Fact]
        public void NInTheCardAddsOnTheChipsDay() => WithMonth((view, store) =>
        {
            var (bar, day) = SecondBar(view);
            DateTime? added = null;
            view.SlotSelected += d => added = d;
            var card = OpenCard(bar);
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), Environment.TickCount, Key.N)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            card.RaiseEvent(args);
            Assert.True(args.Handled, "N in the card was not taken.");
            Assert.Equal(day.AddHours(9), added);
        });

        // GD-006: shrinking one chip rebuilt every cell of the month.
        [Fact]
        public void ShrinkingOneChipKeepsTheOtherCells() => WithMonth((view, store) =>
        {
            int density = CalendarChrome.Density;
            CalendarChrome.Density = 1;   // a title line, so a shrunk chip differs from a full one
            try
            {
            var lunch = new CalendarEvent { Title = "Lunch", Start = new DateTime(2026, 11, 10, 12, 0, 0), End = new DateTime(2026, 11, 10, 13, 0, 0) };
            store.Add(lunch);
            view.Refresh();
            var cells = (IDictionary)typeof(MonthView).GetField("_cells", Flags)!.GetValue(view)!;
            object before = cells[new DateTime(2026, 11, 11)]!;
            typeof(MonthView).GetField("_focusedAppointmentKey", Flags)!.SetValue(view, (Guid?)lunch.SeriesKey);

            Assert.True(view.ToggleFocusedAppointmentSize());

            Assert.Equal(before, cells[new DateTime(2026, 11, 11)]);
            var chip = Chips(view).Single(c => ReferenceEquals(c.Tag, lunch) || (c.Tag as CalendarEvent)?.Id == lunch.Id);
            Assert.Equal(5, chip.Height);
            Assert.True(view.ToggleFocusedAppointmentSize());
            chip = Chips(view).Single(c => (c.Tag as CalendarEvent)?.Id == lunch.Id);
            Assert.True(double.IsNaN(chip.Height) || chip.Height != 5, "The chip did not expand again.");
            }
            finally { CalendarChrome.Density = density; }
        });

        // GD-005: the rolling label used English order in every language.
        [Theory]
        [InlineData("en-US", "November 2 - November 29, 2026")]
        [InlineData("de-DE", "2. November - 29. November 2026")]
        public void TheRollingLabelFollowsTheCulture(string culture, string expected) => OnSta(() =>
        {
            var keep = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                var view = new MonthView();
                typeof(MonthView).GetField("_visibleWeeks", Flags)!.SetValue(view, 4);
                typeof(MonthView).GetField("_rollingStart", Flags)!.SetValue(view, new DateTime(2026, 11, 2));
                Assert.Equal(expected, view.PeriodLabel);
            }
            finally { Thread.CurrentThread.CurrentCulture = keep; }
        });

        private static (Border Bar, DateTime Day) SecondBar(MonthView view)
        {
            var grid = (Grid)view.FindName("CalGrid");
            var start = (DateTime)typeof(MonthView).GetField("_gridStart", Flags)!.GetValue(view)!;
            var bars = grid.Children.OfType<Border>()
                .Where(b => (b.Tag as CalendarEvent)?.Id == Trip.Id)
                .OrderBy(Grid.GetRow).ToArray();
            Assert.Equal(2, bars.Length);
            var bar = bars[1];
            var day = start.AddDays(Grid.GetRow(bar) * 7 + Grid.GetColumn(bar));
            Assert.NotEqual(Trip.Start.Date, day);
            return (bar, day);
        }

        private static Border[] Chips(MonthView view)
        {
            var found = new System.Collections.Generic.List<Border>();
            void Walk(DependencyObject node)
            {
                if (node is CalendarChrome.ChipBorder chip) found.Add(chip);
                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
            }
            Walk((Grid)view.FindName("CalGrid"));
            return found.ToArray();
        }

        private static ContextMenu OpenCard(FrameworkElement element)
        {
            var args = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { element, true }, null);
            args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
            element.RaiseEvent(args);
            return element.ContextMenu!;
        }

        private static void WithMonth(Action<MonthView, EventStore> body) => OnSta(() =>
        {
            string path = Path.Combine(TestScratch.Root, "month-card-" + Guid.NewGuid().ToString("N"), "test.kcal");
            string? shrunk = Settings.Get("MonthShrunkAppointments");
            string? mode = Settings.Get("MonthViewMode");
            var store = new EventStore();
            try
            {
                Settings.Set("MonthShrunkAppointments", "");
                Settings.Set("MonthViewMode", "Calendar");
                store.Open(path);
                store.Add(Trip);
                var view = new MonthView { Anchor = new DateTime(2026, 11, 15) };
                view.Initialize(store);
                body(view, store);
            }
            finally
            {
                store.Close();
                Settings.Set("MonthShrunkAppointments", shrunk ?? "");
                Settings.Set("MonthViewMode", mode ?? "");
                try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { /* best effort */ }
            }
        });

        /// <summary>A key event needs a source; this one has no window behind it.</summary>
        private sealed class FakeSource : PresentationSource
        {
            public override Visual? RootVisual { get => null; set { } }
            public override bool IsDisposed => false;
            protected override CompositionTarget? GetCompositionTargetCore() => null;
        }

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    TestApplication.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The month card test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
