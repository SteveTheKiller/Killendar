using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Killendar.Controls;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    // The website's screenshots, drawn off screen from the Demo calendar: no window is shown and
    // nothing on the desktop changes. Menus and dialogs are their own windows, so they are drawn
    // separately and placed on the picture where the app puts them. Runs only when
    // KILLENDAR_SITE_SHOTS names a folder; KILLENDAR_SITE_SHOT picks one shot by number.
    // Shot 12 is the printed Month page: print the print-month.html written beside shot 01 to
    // PDF and use its first page.
    [Collection("Calendar settings")]
    public sealed class WebsiteScreenshotTests
    {
        [Fact]
        public void DrawTheWebsiteShots()
        {
            string? folder = Environment.GetEnvironmentVariable("KILLENDAR_SITE_SHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            string? only = Environment.GetEnvironmentVariable("KILLENDAR_SITE_SHOT");
            foreach (var shot in WebsiteScreenshotFixture.Shots)
                if (string.IsNullOrWhiteSpace(only) || only == shot.Id)
                    ScratchUiProcess.Run(typeof(WebsiteScreenshotFixture), shot.Id + "|" + folder);
        }
    }

    public sealed class WebsiteScreenshotFixture
    {
        /// <summary>One picture: the window as a user would have it set, and what is open in it.</summary>
        internal sealed class Shot(string Id, string Name, Theme Theme, Accent? Accent, double Width, double Height,
            Action<Scene> Stage, double Scale = 1, string Labels = "Beside", string Icons = "Large", bool Grain = true,
            bool SidebarRight = false, bool TimeZones = false, bool WorkWeek = false)
        {
            public string Id { get; } = Id;
            public string Name { get; } = Name;
            public Theme Theme { get; } = Theme;
            public Accent? Accent { get; } = Accent;
            public double Width { get; } = Width;
            public double Height { get; } = Height;
            public Action<Scene> Stage { get; } = Stage;
            public double Scale { get; } = Scale;
            public string Labels { get; } = Labels;
            public string Icons { get; } = Icons;
            public bool Grain { get; } = Grain;
            public bool SidebarRight { get; } = SidebarRight;
            public bool TimeZones { get; } = TimeZones;
            public bool WorkWeek { get; } = WorkWeek;
        }

        /// <summary>What a shot's staging works with, and what it adds on top of the window.</summary>
        internal sealed class Scene
        {
            public MainWindow Window = null!;
            public EventStore Store = null!;
            public CalendarController Calendar = null!;
            public List<(FrameworkElement Element, Func<Size, Point> At)> Overlays = [];
            public double Width => ((FrameworkElement)Window.Content).ActualWidth;
            public double Height => ((FrameworkElement)Window.Content).ActualHeight;
        }

        private static readonly DateTime Today = DateTime.Today;

        internal static readonly Shot[] Shots =
        [
            new("01", "month-dark-red", Theme.Dark, Accent.Red, 1440, 900, s =>
            {
                s.Calendar.SelectView("Month");
                s.Calendar.GoTo(Today);
            }),
            new("02", "week-decay-time-zones", Theme.Decay, null, 1440, 900, s =>
            {
                s.Calendar.SelectView("Week");
                s.Calendar.GoTo(Thanksgiving);
            }, TimeZones: true),
            new("03", "day-98se-repeat-editor", Theme.SE98, Accent.Green, 1366, 860, s =>
            {
                s.Calendar.SelectView("Day");
                s.Calendar.GoTo(SecondTuesday(Today.Year, Today.Month + 1));
                var series = s.Store.Events.First(e => e.Title == BoardReview);
                Field<AppointmentEditor>(s.Window, "_appointments").Load(series);
            }, Labels: "Under"),
            new("04", "agenda-blood", Theme.Blood, null, 1366, 840, s =>
            {
                s.Calendar.SelectView("Agenda");
                s.Calendar.GoTo(Today);
            }, Labels: "Under", Grain: false),
            new("05", "light-day-panel-right", Theme.Light, Accent.Red, 1440, 900, s =>
            {
                s.Calendar.SelectView("Month");
                s.Calendar.GoTo(Today);
                Call(s.Window, "ShowDayAgenda", Today, (Guid?)null);
            }, SidebarRight: true),
            new("06", "reminders-mourning", Theme.Mourning, null, 1440, 900, s =>
            {
                s.Calendar.SelectView("Week");
                s.Calendar.GoTo(Today);
                var reminders = new RemindersWindow();
                var due = s.Store.GetOnDay(Today).Where(e => !e.AllDay && e.Start.Date == Today && e.Start.Hour >= 8).OrderBy(e => e.Start).Take(2)
                    .Select(e => new DueReminder(e.Id, e.Start, e.Title, e.Location, e.Start, e.End, false, e.Start.AddMinutes(-10)))
                    .ToList();
                var describe = (Func<DueReminder, string>)Delegate.CreateDelegate(typeof(Func<DueReminder, string>), s.Window,
                    typeof(MainWindow).GetMethod("Describe", BindingFlags.NonPublic | BindingFlags.Instance)!);
                reminders.ShowReminders(due, describe);
                s.Overlays.Add((ShowAside(reminders), size => new Point(s.Width - size.Width - 8, s.Height - size.Height - 8)));
            }),
            new("07", "search-cyanotic", Theme.Cyanotic, null, 1366, 860, s =>
            {
                s.Calendar.SelectView("Month");
                s.Calendar.GoTo(Today);
                var search = new SearchDialog(s.Store);
                ((TextBox)search.FindName("QueryBox")).Text = "Northgate";
                s.Overlays.Add((ShowAside(search), size => Centered(s, size)));
            }),
            new("08", "recently-deleted-greed", Theme.Greed, null, 1366, 860, s =>
            {
                s.Calendar.SelectView("Week");
                s.Calendar.GoTo(Today);
                Call(s.Window, "DeletedBtn_Click", s.Window, new RoutedEventArgs());
                AddMenu(s, Field<ContextMenu>(s.Window, "_deletedMenu"));
            }),
            new("09", "killendars-backups-sepulchre", Theme.Sepulchre, null, 1366, 860, s =>
            {
                s.Calendar.SelectView("Month");
                s.Calendar.GoTo(Today);
                var dialog = new KillendarsDialog();
                var content = ShowAside(dialog);
                var list = (ListBox)dialog.FindName("KcList");
                list.SelectedIndex = -1;
                list.SelectedIndex = 0;   // selecting a row shows its last backup
                // The real folder is a scratch folder under this machine's user; show a typical one.
                var status = (TextBlock)dialog.FindName("DlgStatus");
                status.Text = status.Text.Replace(EventStore.DataDir, @"C:\Users\Pat\Documents\Killendar");
                s.Overlays.Add((content, size => Centered(s, size)));
            }),
            new("10", "date-settings-malaise", Theme.Malaise, null, 1440, 900, s =>
            {
                s.Calendar.SelectView("Week");
                s.Calendar.GoTo(Today);
                var menu = Field<DateSettingsMenu>(s.Window, "_dateSettingsMenu");
                menu.Open();
                AddMenu(s, Field<ContextMenu>(menu, "_menu"));
            }, Scale: 0.9, TimeZones: true),
            new("11", "theme-picker-black-teal", Theme.Black, Accent.Teal, 1366, 860, s =>
            {
                s.Calendar.SelectView("Month");
                s.Calendar.GoTo(Today);
                Call(Field<object>(s.Window, "_themePicker"), "Toggle");
                AddMenu(s, (ContextMenu)s.Window.FindName("ThemeMenu"));
            }, Labels: "None"),
        ];

        private const string BoardReview = "Board review";
        private const string TokyoCall = "Call with the Tokyo office";
        private static DateTime Thanksgiving => NthWeekday(Today.Year, 11, DayOfWeek.Thursday, 4);

        public void Run(string scenario)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
                    var parts = scenario.Split('|');
                    Draw(Shots.Single(s => s.Id == parts[0]), parts[1]);
                }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(25000)) throw new TimeoutException("The website shot exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Draw(Shot shot, string folder)
        {
            using var profile = new Profile(shot);
            BuildDemo(profile.Calendars);
            Seed(profile.Calendars);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            ThemeManager.GetSetting = Settings.Get;
            ThemeManager.SetSetting = Settings.Set;
            ThemeManager.Initialize();
            WorkSchedule.Load(null, null);
            TimeZones.Load(shot.TimeZones ? "1" : "0", "Tokyo Standard Time");
            var window = new MainWindow();
            try
            {
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.WindowState = WindowState.Normal;
                window.Left = -30000;
                window.Top = -30000;
                window.Width = shot.Width;
                window.Height = shot.Height;
                window.Show();
                window.Left = -30000;
                window.Top = -30000;
                Settle();
                var scene = new Scene
                {
                    Window = window,
                    Store = Field<EventStore>(window, "_store"),
                    Calendar = Field<CalendarController>(window, "_calendar"),
                };
                shot.Stage(scene);
                Settle();
                Save(scene, Path.Combine(folder, shot.Id + "-" + shot.Name + ".png"));
                if (shot.Id == "01")
                {
                    // The printable Month page, with the app's text loaded, for printing to PDF.
                    var from = new DateTime(Today.Year, Today.Month, 1);
                    string html = HtmlExport.Build(scene.Store.GetInRange(from, from.AddMonths(1)), from, from.AddMonths(1),
                        new HtmlOptions { Layout = HtmlLayout.Month, Locations = true });
                    File.WriteAllText(Path.Combine(folder, "print-month.html"),
                        html.Replace("</head>", "<style>@page { size: Letter landscape; }</style></head>"), new System.Text.UTF8Encoding(false));
                }
                foreach (var (element, _) in scene.Overlays)
                    if (Window.GetWindow(element) is Window owner && owner != window) owner.Close();
            }
            finally { window.Close(); app.Shutdown(); }
        }

        // ---- The calendars ----

        /// <summary>The app's Demo calendar, less its layout stress tests: days packed wall to wall
        /// are there to break layouts and make a busy picture, so they keep their first seven.</summary>
        private static void BuildDemo(string folder)
        {
            var store = new EventStore();
            store.Open(Path.Combine(folder, DemoData.DemoFileName), null);
            var names = (ValueTuple<string, string>[])typeof(DemoData)
                .GetField("DemoCategoryNames", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            foreach (var (from, to) in names) store.RenameCategory(from, to);
            var events = DemoData.Generate(Today)
                .GroupBy(e => e.AllDay ? DateTime.MinValue : e.Start.Date)
                .SelectMany(day => day.Key == DateTime.MinValue ? day : day.OrderBy(e => e.Start).Take(7))
                .ToList();
            store.ImportEvents(events);
            store.Close();
            EventStore.SetActive(DemoData.DemoFileName);
        }

        private static void Seed(string folder)
        {
            int year = Today.Year;
            var holidays = new EventStore();
            holidays.Open(Path.Combine(folder, "Holidays.kcal"));
            foreach (var (title, day) in new[]
            {
                ("Labor Day", NthWeekday(year, 9, DayOfWeek.Monday, 1)),
                ("Indigenous Peoples' Day", NthWeekday(year, 10, DayOfWeek.Monday, 2)),
                ("Veterans Day", new DateTime(year, 11, 11)),
                ("Thanksgiving", NthWeekday(year, 11, DayOfWeek.Thursday, 4)),
                ("Day after Thanksgiving", NthWeekday(year, 11, DayOfWeek.Thursday, 4).AddDays(1)),
                ("Christmas Eve", new DateTime(year, 12, 24)),
                ("Christmas Day", new DateTime(year, 12, 25)),
                ("New Year's Day", new DateTime(year + 1, 1, 1)),
            })
                holidays.Add(new CalendarEvent { Title = title, Start = day, End = day.AddDays(1), AllDay = true });
            holidays.Close();

            // A second calendar shown beside the open one, in its own color.
            var family = new EventStore();
            family.Open(Path.Combine(folder, "Family.kcal"));
            var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
            family.Add(new CalendarEvent
            {
                Title = "Soccer practice", Location = "Riverside Park",
                Start = monday.AddDays(1).AddHours(17.5), End = monday.AddDays(1).AddHours(18.5),
                Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            });
            family.Add(new CalendarEvent
            {
                Title = "Piano lesson", Start = monday.AddDays(2).AddHours(16), End = monday.AddDays(2).AddHours(16.75),
                Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Wednesday],
            });
            family.Add(new CalendarEvent { Title = "Mom's birthday", Start = Today.AddDays(9), End = Today.AddDays(10), AllDay = true });
            family.Add(new CalendarEvent { Title = "Parent-teacher conference", Start = Today.AddDays(6).AddHours(18), End = Today.AddDays(6).AddHours(18.5), Location = "Lincoln Elementary" });
            family.Close();

            var demo = new EventStore();
            demo.Open(Path.Combine(folder, DemoData.DemoFileName));
            // A series that shows off the repeat options: the second Tuesday, moved off days off,
            // with one extra date outside the pattern.
            var first = SecondTuesday(Today.Year, Today.Month);
            demo.Add(new CalendarEvent
            {
                Title = BoardReview, Location = "Conference Room B",
                Start = first.AddHours(14), End = first.AddHours(15.5),
                Repeat = RepeatFreq.Monthly, RepeatMonthWeek = 2, RepeatShift = 1,
                ExtraDates = [SecondTuesday(Today.Year, Today.Month + 2).AddDays(7)],
                Description = "Quarterly numbers, hiring plan, then open questions.",
            });
            // Recently deleted needs something in it.
            foreach (var (title, offset, hour) in new[] { ("Coffee with Sam", 1, 8.5), ("Vendor demo (old time)", 2, 15.0), ("Gym", -1, 18.0) })
            {
                var ev = new CalendarEvent { Title = title, Start = Today.AddDays(offset).AddHours(hour), End = Today.AddDays(offset).AddHours(hour + 1) };
                demo.Add(ev);
                demo.Delete(ev.Id);
            }
            var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            var nextTuesday = Today.AddDays(((int)DayOfWeek.Tuesday - (int)Today.DayOfWeek + 7) % 7 + 7);
            var call = new CalendarEvent
            {
                Title = TokyoCall, Location = "Teams",
                Start = TimeZones.FromZone(nextTuesday.AddHours(7), tokyo),
                Description = "Q1 roadmap with the Tokyo team, first thing in their morning.",
            };
            call.End = call.Start.AddHours(1);
            TimeZones.Apply(call, tokyo.Id);
            demo.Add(call);
            demo.BackupNow(Today.AddHours(7.5));
            demo.Close();
        }

        private static DateTime NthWeekday(int year, int month, DayOfWeek day, int n)
        {
            var first = new DateTime(year, month, 1);
            return first.AddDays(((int)day - (int)first.DayOfWeek + 7) % 7 + 7 * (n - 1));
        }

        private static DateTime SecondTuesday(int year, int month)
        {
            while (month > 12) { month -= 12; year++; }
            return NthWeekday(year, month, DayOfWeek.Tuesday, 2);
        }

        // ---- Drawing ----

        private static void Pump() =>
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        /// <summary>Long enough for the sidebar's slide and every fade to finish.</summary>
        private static void Settle()
        {
            for (int i = 0; i < 24; i++) { Pump(); Thread.Sleep(50); }
        }

        private static Point Centered(Scene s, Size size) => new((s.Width - size.Width) / 2, (s.Height - size.Height) / 2);

        /// <summary>A dialog shown off screen like the main window; its content is what gets drawn.</summary>
        private static FrameworkElement ShowAside(Window dialog)
        {
            dialog.ShowInTaskbar = false;
            dialog.ShowActivated = false;
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = -20000;
            dialog.Top = -20000;
            dialog.Show();
            dialog.Left = -20000;
            dialog.Top = -20000;
            return (FrameworkElement)dialog.Content;
        }

        /// <summary>An open menu, placed by its own placement rule as the app would place it.</summary>
        private static void AddMenu(Scene scene, ContextMenu menu)
        {
            Settle();
            var root = (FrameworkElement)scene.Window.Content;
            scene.Overlays.Add((menu, size =>
            {
                var target = (FrameworkElement)menu.PlacementTarget;
                // On-screen bounds, so the app's zoom is taken into account as the popup's is.
                var bounds = target.TransformToAncestor(root).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                Point offset = menu.Placement == PlacementMode.Custom
                    ? menu.CustomPopupPlacementCallback(size, bounds.Size, new Point())[0].Point
                    : new Point(0, bounds.Height);
                return new Point(bounds.X + offset.X + menu.HorizontalOffset, bounds.Y + offset.Y + menu.VerticalOffset);
            }));
        }

        private static void Save(Scene scene, string path)
        {
            var root = (FrameworkElement)scene.Window.Content;
            root.UpdateLayout();
            Pump();
            double width = root.ActualWidth, height = root.ActualHeight;
            var picture = new DrawingVisual();
            using (var dc = picture.RenderOpen())
            {
                dc.DrawImage(Render(root, scene.Window.Background ?? Brushes.Black, width, height), new Rect(0, 0, width, height));
                foreach (var (element, at) in scene.Overlays)
                {
                    element.UpdateLayout();
                    var size = new Size(Math.Ceiling(element.ActualWidth), Math.Ceiling(element.ActualHeight));
                    var point = at(size);
                    dc.DrawImage(Render(element, Brushes.Transparent, size.Width, size.Height), new Rect(point, size));
                }
            }
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(picture);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
        }

        private static BitmapSource Render(FrameworkElement element, Brush background, double width, double height)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Max(1, width), (int)Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
            var back = new DrawingVisual();
            using (var dc = back.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            bitmap.Render(back);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            bitmap.Render(element);
            bitmap.Freeze();
            return bitmap;
        }

        private static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

        private static void Call(object owner, string name, params object?[] args)
        {
            // By name and by what the arguments fit, since some names are overloaded.
            var method = owner.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
                             m.GetParameters().Zip(args, (p, a) => a == null ? !p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null
                                                                            : p.ParameterType.IsInstanceOfType(a)).All(fits => fits));
            method.Invoke(owner, args);
        }

        /// <summary>A settings file of its own, so the user's real settings are never read or changed.</summary>
        private sealed class Profile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\site-shots", Guid.NewGuid().ToString("N"));
            public string Calendars => Path.Combine(_root, "calendars");
            public Profile(Shot shot)
            {
                Directory.CreateDirectory(Calendars);
                var settings = typeof(Settings);
                const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
                settings.GetField("Dir", Static)!.SetValue(null, _root);
                settings.GetField("FilePath", Static)!.SetValue(null, Path.Combine(_root, "settings.json"));
                var values = new Dictionary<string, string>
                {
                    ["KillendarDataDir"] = Calendars,
                    ["CheckForUpdatesOnStartup"] = "0",
                    ["CloseToTray"] = "0",
                    ["Theme"] = shot.Theme.ToString(),
                    ["FilmGrain"] = shot.Grain ? "1" : "0",
                    ["AppScale"] = shot.Scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    ["ToolbarLabels"] = shot.Labels,
                    ["ToolbarIconSize"] = shot.Icons,
                    ["SidebarSide"] = shot.SidebarRight ? "Right" : "Left",
                    ["WorkWeek"] = shot.WorkWeek ? "1" : "0",
                    ["TimeGridDensity"] = "1",
                    [ShownCalendars.Setting] = "Family.kcal|#8E6CC9",
                    [Holidays.Setting] = "Holidays.kcal",
                    [TimeZones.ShowSetting] = shot.TimeZones ? "1" : "0",
                    [TimeZones.SecondSetting] = "Tokyo Standard Time",
                };
                if (shot.Accent is Accent accent)
                    values[shot.Theme == Theme.SE98 ? "98SEAccent" : shot.Theme + "Accent"] = accent.ToString();
                settings.GetField("_cache", Static)!.SetValue(null, values);
            }
            public void Dispose()
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(_root, true); } catch (IOException) { }
            }
        }
    }
}
