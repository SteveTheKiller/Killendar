using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    // Pictures of the main window for a person to look at, drawn off screen: no window is shown.
    // Runs only when KILLENDAR_SNAPSHOTS names a folder for the .png files; otherwise it does nothing.
    [Collection("Calendar settings")]
    public sealed class RenderedSnapshotTests
    {
        [Fact]
        public void DrawTheNewScreens()
        {
            string? folder = Environment.GetEnvironmentVariable("KILLENDAR_SNAPSHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            foreach (var theme in new[] { "Dark", "Light" })
                ScratchUiProcess.Run(typeof(RenderedSnapshotFixture), theme + "|" + folder);
        }
    }

    public sealed class RenderedSnapshotFixture
    {
        public void Run(string scenario)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
                    var parts = scenario.Split('|');
                    Exercise(parts[0], parts[1]);
                }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(60000)) throw new TimeoutException("The snapshot fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private const double Width = 1400, Height = 900;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise(string theme, string folder)
        {
            using var profile = new SnapshotProfile(theme);
            Seed(profile.Calendars);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/" + theme + ".xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            ThemeManager.GetSetting = Settings.Get;
            ThemeManager.SetSetting = Settings.Set;
            // The theme as the app builds it: the accent, the derived brushes (dropdowns among
            // them) and the title sizes come from here, not from the theme file alone.
            ThemeManager.Apply((Theme)Enum.Parse(typeof(Theme), theme));
            WorkSchedule.Load(null, null);
            TimeZones.Load("1", "Tokyo Standard Time");
            var window = new MainWindow();
            try
            {
                // Loaded, so it lays out as it does for real, but far off every screen, with no
                // taskbar button and never taking focus: nothing on the desktop changes.
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.WindowState = WindowState.Normal;
                window.Left = -30000;
                window.Top = -30000;
                window.Width = Width;
                window.Height = Height;
                window.Show();
                window.Left = -30000;
                window.Top = -30000;
                for (int i = 0; i < 5; i++) Pump();
                var calendar = Field<CalendarController>(window, "_calendar");
                string prefix = Path.Combine(folder, theme.ToLowerInvariant() + "-");

                calendar.GoTo(new DateTime(2026, 12, 15));
                calendar.SelectView("Month");
                Save(window, prefix + "1-month-holidays-and-shown-calendar.png");

                calendar.SelectView("Week");
                calendar.GoTo(new DateTime(2026, 12, 21));
                Save(window, prefix + "2-week-second-time-zone.png");

                var store = Field<EventStore>(window, "_store");
                var series = store.Events.Single(e => e.Title == "Market stall");
                var editor = Field<AppointmentEditor>(window, "_appointments");
                editor.Load(series);
                Save(window, prefix + "3-editor-repeat-section.png");

                var zoned = store.Events.Single(e => e.Title == "Call with Tokyo");
                editor.Load(zoned);
                Save(window, prefix + "4-editor-time-zone.png");

                var menu = Field<Killendar.Controls.DateSettingsMenu>(window, "_dateSettingsMenu");
                SavePanel(window, menu, "_menu", prefix + "5-date-settings.png");

                Call(window, "BuildShownMenu");
                var shown = Field<ContextMenu>(window, "_shownMenu");
                SaveMenu(window, shown, prefix + "6-show-alongside.png");
            }
            finally { window.Close(); app.Shutdown(); }
        }

        private static void Seed(string folder)
        {
            var home = new EventStore();
            home.Open(Path.Combine(folder, "Home.kcal"));
            var day = new DateTime(2026, 12, 1);
            home.Add(new CalendarEvent { Title = "Dentist", Start = day.AddDays(2).AddHours(10), End = day.AddDays(2).AddHours(11), Categories = "Red", Location = "Main Street 4" });
            home.Add(new CalendarEvent
            {
                Title = "Market stall", Start = new DateTime(2026, 12, 5, 8, 0, 0), End = new DateTime(2026, 12, 5, 12, 0, 0),
                Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Saturday], RepeatShift = 1,
                ExtraDates = [new DateTime(2026, 12, 23), new DateTime(2026, 12, 30)],
            });
            home.Add(new CalendarEvent
            {
                Title = "Board review", Start = new DateTime(2026, 12, 8, 14, 0, 0), End = new DateTime(2026, 12, 8, 15, 0, 0),
                Repeat = RepeatFreq.Monthly, RepeatMonthWeek = 2,
            });
            var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            var call = new CalendarEvent { Title = "Call with Tokyo", Start = TimeZones.FromZone(new DateTime(2026, 12, 22, 9, 0, 0), tokyo) };
            call.End = call.Start.AddHours(1);
            TimeZones.Apply(call, tokyo.Id);
            home.Add(call);
            home.Close();

            var holidays = new EventStore();
            holidays.Open(Path.Combine(folder, "Holidays.kcal"));
            holidays.Add(new CalendarEvent { Title = "Christmas Day", Start = new DateTime(2026, 12, 25), End = new DateTime(2026, 12, 26), AllDay = true });
            holidays.Add(new CalendarEvent { Title = "Boxing Day", Start = new DateTime(2026, 12, 26), End = new DateTime(2026, 12, 27), AllDay = true });
            holidays.Close();

            var work = new EventStore();
            work.Open(Path.Combine(folder, "Work.kcal"));
            work.Add(new CalendarEvent { Title = "Sprint planning", Start = new DateTime(2026, 12, 14, 9, 30, 0), End = new DateTime(2026, 12, 14, 11, 0, 0) });
            work.Add(new CalendarEvent { Title = "Team lunch", Start = new DateTime(2026, 12, 22, 12, 0, 0), End = new DateTime(2026, 12, 22, 13, 30, 0) });
            work.Close();
        }

        private static void Pump() =>
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        private static void Save(Window window, string path)
        {
            // Long enough for the sidebar's slide and the fades to finish.
            for (int i = 0; i < 20; i++) { Pump(); Thread.Sleep(50); }
            var root = (FrameworkElement)window.Content;
            root.UpdateLayout();
            Pump();
            Write(root, window.Background ?? Brushes.Black, root.ActualWidth, root.ActualHeight, path);
        }

        private static void SavePanel(Window window, object owner, string menuField, string path)
        {
            Call(owner, "Build");
            var menu = Field<ContextMenu>(owner, menuField);
            SaveMenu(window, menu, path);
        }

        /// <summary>A flyout's panel, lifted out of its menu and drawn on the menu's own surface.</summary>
        private static void SaveMenu(Window window, ContextMenu menu, string path)
        {
            var item = (MenuItem)menu.Items[0];
            var panel = (UIElement)item.Header;
            item.Header = null;
            var surface = new Border
            {
                Child = panel,
                Padding = new Thickness(4),
                BorderThickness = new Thickness(1),
                Background = Application.Current.TryFindResource("MenuBackgroundBrush") as Brush ?? Brushes.Gray,
                BorderBrush = Application.Current.TryFindResource("MenuBorderBrush") as Brush ?? Brushes.Gray,
            };
            System.Windows.Documents.TextElement.SetForeground(surface, TextElement_Foreground());
            surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = surface.DesiredSize;
            surface.Arrange(new Rect(size));
            surface.UpdateLayout();
            Pump();
            Write(surface, Brushes.Transparent, Math.Ceiling(size.Width), Math.Ceiling(size.Height), path);
        }

        private static Brush TextElement_Foreground() =>
            Application.Current.TryFindResource("TextBrush") as Brush ?? Brushes.White;

        private static void Write(FrameworkElement element, Brush background, double width, double height, string path)
        {
            var back = new DrawingVisual();
            using (var dc = back.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(back);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
        }

        private static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

        private static void Call(object owner, string name) =>
            owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, null);

        private sealed class SnapshotProfile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\snapshots", Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            public string Calendars => Path.Combine(_root, "calendars");
            public SnapshotProfile(string theme)
            {
                Directory.CreateDirectory(Calendars);
                _dir.SetValue(null, _root);
                _path.SetValue(null, Path.Combine(_root, "settings.json"));
                _cache.SetValue(null, new Dictionary<string, string>
                {
                    ["KillendarDataDir"] = Calendars,
                    ["ActiveKillendar"] = "Home.kcal",
                    ["CheckForUpdatesOnStartup"] = "0",
                    ["CloseToTray"] = "0",
                    ["Theme"] = theme,
                    [ShownCalendars.Setting] = "Work.kcal|#8E6CC9",
                    [Holidays.Setting] = "Holidays.kcal",
                    [TimeZones.ShowSetting] = "1",
                    [TimeZones.SecondSetting] = "Tokyo Standard Time",
                    ["TimeGridDensity"] = "1",
                });
            }
            public void Dispose()
            {
                SqliteCleanup();
                try { Directory.Delete(_root, true); } catch (IOException) { }
            }
            private static void SqliteCleanup() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }
}
