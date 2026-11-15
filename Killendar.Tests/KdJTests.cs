using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Killendar.Shell;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>KD-J: the review leftovers that were real by reading the code. Everything here is
    /// headless: nothing shows a window.</summary>
    [Collection("Calendar settings")]
    public sealed class KdJTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        // ---- Month view at the ends of the date range (and GD-004) ----

        [Theory]
        [InlineData(9999, 12, 15)]
        [InlineData(1, 1, 15)]
        public void MonthViewBuildsAtTheEndsOfTheDateRange(int year, int month, int day) => OnSta(() =>
        {
            string path = Path.Combine(TestScratch.Root, "kdj-month-" + Guid.NewGuid().ToString("N"), "t.kcal");
            string? mode = Settings.Get("MonthViewMode");
            var store = new EventStore();
            try
            {
                Settings.Set("MonthViewMode", "Calendar");
                store.Open(path);
                // A span that touches the edge day, which the week's multi-day lane math must survive.
                var edgeDay = month == 12 ? new DateTime(9999, 12, 31) : new DateTime(1, 1, 1);
                store.Add(new CalendarEvent
                {
                    Title = "Edge", AllDay = true,
                    Start = month == 12 ? edgeDay.AddDays(-2) : edgeDay,
                    End = month == 12 ? DateTime.MaxValue : edgeDay.AddDays(2),
                });
                var view = new MonthView { Anchor = new DateTime(year, month, day) };
                var failure = Record.Exception(() => view.Initialize(store));
                Assert.Null(failure);
                var cells = (IDictionary)typeof(MonthView).GetField("_cells", Flags)!.GetValue(view)!;
                var edge = month == 12 ? new DateTime(9999, 12, 31) : new DateTime(1, 1, 1);
                Assert.True(cells.Contains(edge), "The last supported day has no cell.");
            }
            finally
            {
                Settings.Set("MonthViewMode", mode ?? "");
                store.Close();
                try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { /* best effort */ }
            }
        });

        [Fact]
        public void SteppingPastTheLastMonthStaysPut() => OnSta(() =>
        {
            string? mode = Settings.Get("MonthViewMode");
            try
            {
                Settings.Set("MonthViewMode", "Calendar");
                var view = new MonthView { Anchor = new DateTime(9999, 12, 15) };
                var last = new DateTime(9999, 12, 15);
                DateTime stepped = default;
                Assert.Null(Record.Exception(() => stepped = view.Step(last, 1)));
                Assert.Equal(last, stepped);
            }
            finally { Settings.Set("MonthViewMode", mode ?? ""); }
        });

        [Fact]
        public void MonthDropNearTheFirstDayStaysInRange()
        {
            // A long appointment grabbed late in its span and dropped on the first supported day:
            // the delta reaches before the calendar's first day.
            DateTime result = default;
            Assert.Null(Record.Exception(() => result = MonthView.DroppedStart(
                new DateTime(1, 1, 3, 9, 0, 0), new DateTime(1, 1, 10), new DateTime(1, 1, 1), false)));
            Assert.Equal(new DateTime(1, 1, 1, 9, 0, 0), result);
        }

        [Fact]
        public void MonthDropNearTheLastDayStaysInRange()
        {
            DateTime result = default;
            Assert.Null(Record.Exception(() => result = MonthView.DroppedStart(
                new DateTime(9999, 12, 31, 9, 0, 0), new DateTime(9999, 12, 20), new DateTime(9999, 12, 31), true)));
            Assert.Equal(new DateTime(9999, 12, 31), result);
        }

        // ---- GD-007: the hover grain brush is built once per cell position ----

        [Fact]
        public void HoveringACellTwiceReusesItsGrainBrush() => OnSta(() =>
        {
            string path = Path.Combine(TestScratch.Root, "kdj-grain-" + Guid.NewGuid().ToString("N"), "t.kcal");
            string? mode = Settings.Get("MonthViewMode");
            var store = new EventStore();
            var resources = Application.Current.Resources;
            object? oldTile = resources["GrainTileBrush"];
            try
            {
                Settings.Set("MonthViewMode", "Calendar");
                resources["GrainTileBrush"] = new ImageBrush
                {
                    TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 256, 256), Stretch = Stretch.None,
                };
                store.Open(path);
                var view = new MonthView { Anchor = new DateTime(2026, 11, 15) };
                view.Initialize(store);
                var cells = (IDictionary)typeof(MonthView).GetField("_cells", Flags)!.GetValue(view)!;
                var cell = (Border)cells[new DateTime(2026, 11, 10)]!.GetType().GetField("Item1")!.GetValue(cells[new DateTime(2026, 11, 10)])!;

                Brush? Hover()
                {
                    cell.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                    var grain = Descendants(cell).OfType<Border>().Single(b => b.Background is TileBrush);
                    var brush = grain.Background;
                    cell.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
                    return brush;
                }

                var first = Hover();
                var second = Hover();
                Assert.NotNull(first);
                Assert.Same(first, second);
            }
            finally
            {
                if (oldTile == null) resources.Remove("GrainTileBrush"); else resources["GrainTileBrush"] = oldTile;
                Settings.Set("MonthViewMode", mode ?? "");
                store.Close();
                try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { /* best effort */ }
            }
        });

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject node)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var deeper in Descendants(child)) yield return deeper;
            }
        }

        // ---- GG-004: the minimum size follows the work area it is given ----

        [Fact]
        public void MinimumSizeClampsToTheGivenWorkArea()
        {
            var second = new Rect(1920, 0, 1000, 700);

            var (width, height) = MainWindow.ClampMinSize(1400, 900, second);

            Assert.Equal(1000, width);
            Assert.Equal(700, height);
        }

        [Fact]
        public void MinimumSizeThatFitsIsLeftAlone()
        {
            var (width, height) = MainWindow.ClampMinSize(600, 400, new Rect(0, 0, 1920, 1040));

            Assert.Equal(600, width);
            Assert.Equal(400, height);
        }

        // ---- GG-006: search confirms before it moves anything ----

        [Fact]
        public void SearchKeepsTheCalendarWhenTheDraftIsKept()
        {
            var steps = new System.Collections.Generic.List<string>();

            bool shown = SearchOpen.Run(() => { steps.Add("confirm"); return false; },
                                        () => steps.Add("goto"), () => steps.Add("show"));

            Assert.False(shown);
            Assert.Equal(new[] { "confirm" }, steps);
        }

        [Fact]
        public void SearchConfirmsThenMovesThenShows()
        {
            var steps = new System.Collections.Generic.List<string>();

            bool shown = SearchOpen.Run(() => { steps.Add("confirm"); return true; },
                                        () => steps.Add("goto"), () => steps.Add("show"));

            Assert.True(shown);
            Assert.Equal(new[] { "confirm", "goto", "show" }, steps);
        }

        // ---- GE-004: falling back to the previous Killendar keeps its password ----

        [Fact]
        public void FallingBackToAnEncryptedKillendarDoesNotAskForItsPasswordAgain() => OnSta(() =>
        {
            // A regression here pops a real password dialog, so the test runs only where a hidden
            // desktop or a lab VM sets KILLENDAR_WINDOW_TESTS=1.
            if (Environment.GetEnvironmentVariable("KILLENDAR_WINDOW_TESTS") != "1") return;
            string root = Path.Combine(TestScratch.Root, "kdj-fallback", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string? savedDir = Settings.Get("KillendarDataDir"), savedActive = Settings.Get("ActiveKillendar");
            var store = new EventStore();
            try
            {
                EventStore.SetDataDir(root);
                string previous = Path.Combine(root, "Previous.kcal");
                store.Open(previous);
                store.SetPassword("pw");
                store.Close();
                // A folder where a calendar file is expected fails to open without a prompt.
                Directory.CreateDirectory(Path.Combine(root, "Selected.kcal"));
                EventStore.SetActive("Previous.kcal");
                store.Open(previous, "pw");
                var host = new SecurityHostFake();
                var security = new SecurityController(host, store);
                security.RememberPassword("pw");

                security.ApplyKillendarSelection("Selected.kcal", "Previous.kcal", root, previous);

                Assert.True(store.IsOpen, "The previous Killendar did not reopen.");
                Assert.Equal(previous, store.FilePath, ignoreCase: true);
                Assert.True(security.RemembersPassword, "The fallback forgot the password it had just used.");
            }
            finally
            {
                store.Close();
                Settings.Set("KillendarDataDir", savedDir ?? "");
                Settings.Set("ActiveKillendar", savedActive ?? "");
                try { Directory.Delete(root, true); } catch { /* best effort */ }
            }
        });

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
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The KD-J test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
