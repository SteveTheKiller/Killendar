using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Killendar.Features;
using Killendar.Services;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>KD-H: review findings that needed a red test before any fix (GD-004, GE-006, GE-008,
    /// GL-006). Each test pins the behavior a user would see.</summary>
    [Collection("Calendar settings")]
    public sealed class KdHInvestigationTests
    {
        // GD-004 was dropped: a drop past either end of the date range cannot be reached. The month
        // grid itself throws while it is built for December 9999 (NaturalRowCount), long before a
        // chip can be dragged. Only the week grid's own clamp is kept as a guard.
        [Fact]
        public void WeekDropAtTheLastSupportedDayStaysInRange()
        {
            var start = new DateTime(9999, 12, 31, 9, 0, 0);
            DateTime result = default;
            Assert.Null(Record.Exception(() => result = TimeGridView.DroppedStart(
                start, new DateTime(9999, 12, 31, 9, 0, 0), new DateTime(9999, 12, 31, 23, 59, 0), 30)));
            Assert.Equal(new DateTime(9999, 12, 31, 23, 30, 0), result);
        }

        // GE-008: Day view's title used the US order for every language.
        [Theory]
        [InlineData("de-DE", "Samstag, 14. November 2026")]
        [InlineData("en-GB", "Saturday, 14 November 2026")]
        [InlineData("en-US", "Saturday, November 14, 2026")]
        public void DayViewLabelFollowsTheCulture(string culture, string expected) => OnSta(() =>
        {
            var keep = Thread.CurrentThread.CurrentCulture;
            try
            {
                var info = new CultureInfo(culture);
                Thread.CurrentThread.CurrentCulture = info;
                var view = new DayView { Anchor = new DateTime(2026, 11, 14) };
                Assert.Equal(expected, view.PeriodLabel);
            }
            finally { Thread.CurrentThread.CurrentCulture = keep; }
        });

        // GE-006: adopting a file from the data folder refreshed the view twice.
        [Fact]
        public void AdoptingADataFolderFileRefreshesTheViewOnce()
        {
            string root = Path.Combine(TestScratch.Root, "kdh-adopt", Guid.NewGuid().ToString("N"));
            string? savedDir = Settings.Get("KillendarDataDir");
            string? savedActive = Settings.Get("ActiveKillendar");
            var store = new EventStore();
            try
            {
                Directory.CreateDirectory(root);
                EventStore.SetDataDir(root);
                var other = new EventStore();
                other.Open(Path.Combine(root, "Other.kcal"));
                other.Close();
                store.Open(Path.Combine(root, "First.kcal"));
                EventStore.SetActive("First.kcal");
                var host = new SecurityHostFake();
                var security = new SecurityController(host, store);
                App.PendingOpenFile = Path.Combine(root, "Other.kcal");

                security.AdoptPendingFile();

                Assert.Equal(Path.Combine(root, "Other.kcal"), store.FilePath, ignoreCase: true);
                Assert.Equal(1, host.Refreshes);
            }
            finally
            {
                App.PendingOpenFile = null;
                store.Close();
                Settings.Set("KillendarDataDir", savedDir ?? "");
                Settings.Set("ActiveKillendar", savedActive ?? "");
                try { Directory.Delete(root, true); } catch { /* best effort */ }
            }
        }

        // GL-006: a canceled password leaves the picked calendar chosen and locked, so the combo
        // (which reselects by Holidays.Name) and the state under it still agree.
        [Fact]
        public void CanceledHolidayPasswordLeavesTheChoiceAndTheLockedStateInStep()
        {
            string root = Path.Combine(TestScratch.Root, "kdh-holiday", Guid.NewGuid().ToString("N"));
            string? savedDir = Settings.Get("KillendarDataDir");
            string? savedHoliday = Settings.Get(Holidays.Setting);
            var open = new EventStore();
            try
            {
                Directory.CreateDirectory(root);
                EventStore.SetDataDir(root);
                var locked = new EventStore();
                locked.Open(Path.Combine(root, "Locked.kcal"), "secret");
                locked.Close();

                Holidays.Choose("Locked.kcal", open, "Main.kcal");

                Assert.Equal(Holidays.State.Locked, Holidays.Status);
                Assert.Equal("Locked.kcal", Holidays.Name);
                Assert.Equal("Locked.kcal", Settings.Get(Holidays.Setting));
            }
            finally
            {
                Holidays.Choose("", open, "Main.kcal");
                open.Close();
                Settings.Set(Holidays.Setting, savedHoliday ?? "");
                Settings.Set("KillendarDataDir", savedDir ?? "");
                try { Directory.Delete(root, true); } catch { /* best effort */ }
            }
        }

        // GD-008 was dropped: the richest month density copies the resolved text brush, but a theme
        // switch repaints through Refresh (CalendarHost), which rebuilds the chip from the new one.
        [Fact]
        public void RichestMonthChipFollowsATextBrushChangeOnRefresh() => OnSta(() =>
        {
            string path = Path.Combine(TestScratch.Root, "kdh-chip-" + Guid.NewGuid().ToString("N"), "t.kcal");
            string? mode = Settings.Get("MonthViewMode");
            int density = CalendarChrome.Density;
            var store = new EventStore();
            var resources = System.Windows.Application.Current.Resources;
            object? oldBrush = resources["TextBrush"];
            try
            {
                Settings.Set("MonthViewMode", "Calendar");
                CalendarChrome.Density = 3;
                resources["TextBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                store.Open(path);
                store.Add(new Killendar.Models.CalendarEvent
                {
                    Title = "Lunch", Start = new DateTime(2026, 11, 10, 12, 0, 0), End = new DateTime(2026, 11, 10, 13, 0, 0)
                });
                var view = new MonthView { Anchor = new DateTime(2026, 11, 15) };
                view.Initialize(store);
                Assert.Equal(System.Windows.Media.Colors.Red, LunchForeground(view));

                resources["TextBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);
                view.Refresh();

                Assert.Equal(System.Windows.Media.Colors.Blue, LunchForeground(view));
            }
            finally
            {
                if (oldBrush == null) resources.Remove("TextBrush"); else resources["TextBrush"] = oldBrush;
                CalendarChrome.Density = density;
                Settings.Set("MonthViewMode", mode ?? "");
                store.Close();
                try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { /* best effort */ }
            }
        });

        private static System.Windows.Media.Color LunchForeground(MonthView view)
        {
            var found = new System.Collections.Generic.List<CalendarChrome.ChipBorder>();
            void Walk(System.Windows.DependencyObject node)
            {
                if (node is CalendarChrome.ChipBorder chip) found.Add(chip);
                foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<System.Windows.DependencyObject>()) Walk(child);
            }
            Walk((System.Windows.Controls.Grid)view.FindName("CalGrid"));
            var lunch = found.Single(c => (c.Tag as Killendar.Models.CalendarEvent)?.Title == "Lunch");
            var lines = (System.Windows.Controls.StackPanel)lunch.Child;
            return ((System.Windows.Media.SolidColorBrush)((System.Windows.Controls.TextBlock)lines.Children[1]).Foreground).Color;
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
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The KD-H test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
