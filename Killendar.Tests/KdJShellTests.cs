using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Killendar.Models;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>KD-J items that need the real main window object (built, never shown): the panel
    /// ride bookkeeping, the template Undo, and the dead repeat summary. Each scenario runs in its
    /// own scratch process, like the other MainWindow fixtures.</summary>
    [Collection("Calendar settings")]
    public sealed class KdJShellTests
    {
        [Fact]
        public void SwitchingViewsKeepsPreviousAndNextShortcutHints()
            => ScratchUiProcess.Run(typeof(KdJShellFixture), "navigation-hints");

        [Fact]
        public void ClosingAfterTheUserResizedClearsTheBorrowedLeftShift()
            => ScratchUiProcess.Run(typeof(KdJShellFixture), "resized-shift");

        [Fact]
        public void ClosingAMaximizedWindowClearsTheBorrowedLeftShift()
            => ScratchUiProcess.Run(typeof(KdJShellFixture), "maximized-shift");

        [Fact]
        public void ClosingWhileTheOpenRideIsRunningKeepsRiding()
            => ScratchUiProcess.Run(typeof(KdJShellFixture), "close-mid-open");

        [Fact]
        public void DeletingATemplateOffersAnUndoThatRestoresIt()
            => ScratchUiProcess.Run(typeof(KdJShellFixture), "template-undo");

        [Fact]
        public void TheDeadRepeatSummaryIsGone()
            => ScratchUiProcess.Run(typeof(KdJShellFixture), "repeat-summary");
    }

    public sealed class KdJShellFixture
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

        public void Run(string scenario)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
                    Exercise(scenario);
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(60000)) throw new TimeoutException("The KD-J shell fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise(string scenario)
        {
            using var profile = new Profile();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Controls/AppStyles.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            Killendar.Services.AppTheme.Configure();
            ThemeManager.GetSetting = Settings.Get;
            ThemeManager.SetSetting = Settings.Set;
            var window = new MainWindow();
            try
            {
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1200, 800));
                root.Arrange(new Rect(0, 0, 1200, 800));
                root.UpdateLayout();
                switch (scenario)
                {
                    case "navigation-hints": NavigationHints(window); break;
                    case "resized-shift": ResizedShift(window); break;
                    case "maximized-shift": MaximizedShift(window); break;
                    case "close-mid-open": CloseMidOpen(window); break;
                    case "template-undo": TemplateUndo(window, profile); break;
                    case "repeat-summary": RepeatSummaryIsGone(window); break;
                    default: throw new ArgumentException(scenario);
                }
                Assert.False(window.IsLoaded, "The fixture must not run the window Loaded path.");
            }
            finally { app.Shutdown(); }
        }

        private static T Get<T>(MainWindow w, string field) =>
            (T)typeof(MainWindow).GetField(field, Instance)!.GetValue(w)!;

        private static void NavigationHints(MainWindow window)
        {
            foreach (string view in new[] { "TabWeek", "TabDay", "TabYear", "TabMonth" })
            {
                ((Button)window.FindName(view)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (var (button, binding) in new[] { ("PrevBtn", "Str_KS_Prev"), ("NextBtn", "Str_KS_Next") })
                    Assert.EndsWith("(" + MainWindow.ShortcutKeys(binding) + ")",
                        ((Button)window.FindName(button)).ToolTip?.ToString());
            }
        }

        private static void Set(MainWindow w, string field, object value) =>
            typeof(MainWindow).GetField(field, Instance)!.SetValue(w, value);

        private static void Shrink(MainWindow w) => typeof(MainWindow).GetMethod("ShrinkAfterSidebar", Instance)!.Invoke(w, null);

        // GG-002: the user resized while the panel was open, so there is nothing to give back,
        // and the borrowed left shift must not wait for some later ride to apply it.
        private static void ResizedShift(MainWindow w)
        {
            Set(w, "_rideGrow", 100.0);
            Set(w, "_rideBase", 800.0);
            Set(w, "_rideLeftShift", 40.0);   // ActualWidth is 0, nowhere near base + grow

            Shrink(w);

            Assert.Equal(0.0, Get<double>(w, "_rideGrow"));
            Assert.Equal(0.0, Get<double>(w, "_rideLeftShift"));
        }

        private static void MaximizedShift(MainWindow w)
        {
            w.WindowState = WindowState.Maximized;
            Set(w, "_rideGrow", 100.0);
            Set(w, "_rideLeftShift", 40.0);

            Shrink(w);

            Assert.Equal(0.0, Get<double>(w, "_rideGrow"));
            Assert.Equal(0.0, Get<double>(w, "_rideLeftShift"));
        }

        // GG-003: the panel is still riding open when it is closed. The width tracks the column
        // every frame, so the same ride carries it back; dropping the borrow snaps the window.
        private static void CloseMidOpen(MainWindow w)
        {
            Set(w, "_riding", true);          // the open ride is running
            Set(w, "_rideGrow", 100.0);
            Set(w, "_rideBase", 800.0);
            Set(w, "_rideLeftShift", 40.0);   // ActualWidth is 0: mid-slide, not at base + grow

            Shrink(w);

            Assert.Equal(100.0, Get<double>(w, "_rideGrow"));
            Assert.Equal(40.0, Get<double>(w, "_rideLeftShift"));
        }

        // GG-001: a deleted template can be put back from the status bar.
        private static void TemplateUndo(MainWindow w, Profile profile)
        {
            var store = Get<EventStore>(w, "_store");
            store.Open(Path.Combine(profile.Calendars, "fixture.kcal"));
            store.SaveTemplate(new CalendarEvent
            {
                Title = "Weekly review", Location = "Desk", Description = "Notes",
                Start = new DateTime(2026, 11, 10, 9, 0, 0), End = new DateTime(2026, 11, 10, 10, 30, 0),
            });
            var undo = (Button)w.FindName("StatusUndoBtn")!;
            Assert.Equal(Visibility.Collapsed, undo.Visibility);

            var menu = KillerFamily.Controls.CardBuilder.Menu(280);
            typeof(MainWindow).GetMethod("AddTemplateItems", Instance)!.Invoke(w, new object[] { menu, new DateTime(2026, 11, 12) });
            var delete = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Delete template");
            var item = delete.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Weekly review");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Empty(store.ListTemplates());
            Assert.Equal(Visibility.Visible, undo.Visibility);
            Assert.Equal("Undo", undo.Content);
            Assert.Contains("Weekly review", ((TextBlock)w.FindName("StatusText")!).Text);

            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var back = Assert.Single(store.ListTemplates());
            Assert.Equal("Weekly review", back.Title);
            Assert.Equal("Desk", back.Location);
            Assert.Equal("Notes", back.Description);
            Assert.Equal(TimeSpan.FromMinutes(90), back.End - back.Start);
            Assert.Equal(Visibility.Collapsed, undo.Visibility);
            store.Close();
        }

        // GF-004: nothing reads the repeat summary, so neither it nor its updater stays.
        private static void RepeatSummaryIsGone(MainWindow w)
        {
            Assert.Null(w.FindName("RepeatSummary"));
            Assert.Null(typeof(MainWindow).GetMethod("UpdateRepeatSummary", Instance));
        }

        private sealed class Profile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\kdj-shell",
                Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly object? _oldDir, _oldPath, _oldCache;
            public string Calendars => Path.Combine(_root, "calendars");
            public Profile()
            {
                Directory.CreateDirectory(Calendars);
                _oldDir = _dir.GetValue(null); _oldPath = _path.GetValue(null); _oldCache = _cache.GetValue(null);
                _dir.SetValue(null, _root);
                _path.SetValue(null, Path.Combine(_root, "settings.json"));
                _cache.SetValue(null, new Dictionary<string, string>
                {
                    ["KillendarDataDir"] = Calendars,
                    ["ActiveKillendar"] = Path.Combine(Calendars, "fixture.kcal"),
                    ["CheckForUpdatesOnStartup"] = "0",
                    ["CloseToTray"] = "0"
                });
            }
            public void Dispose()
            {
                _dir.SetValue(null, _oldDir); _path.SetValue(null, _oldPath); _cache.SetValue(null, _oldCache);
                try { Directory.Delete(_root, true); } catch { /* best effort */ }
            }
        }
    }
}
