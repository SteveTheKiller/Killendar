using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Killendar.Models;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The sidebar day list holds rows from the shown Killendars as well as the open one,
    /// so it rebuilds when a shown Killendar changes state. Ctrl+L used to lock an encrypted shown
    /// Killendar and leave its appointments listed by name.</summary>
    public sealed class ShownCalendarsAgendaWiringTests
    {
        [Fact]
        public void AShownCalendarChangeRebuildsTheOpenDayList()
        {
            string menu = File.ReadAllText(Path.Combine(Root(), "Shell", "ShownCalendarsMenu.cs"));
            Match init = Regex.Match(menu, @"void InitShownCalendars\(\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(init.Success, "ShownCalendarsMenu.cs has no InitShownCalendars.");
            // The handler runs from the subscription to the menu set-up that follows it.
            Match handler = Regex.Match(init.Groups["body"].Value, @"ShownCalendars\.Changed \+=(?<handler>.*?)_shownMenu = ", RegexOptions.Singleline);
            Assert.True(handler.Success, "InitShownCalendars does not handle ShownCalendars.Changed.");
            Assert.Contains("BuildDayAgendaRows()", handler.Groups["handler"].Value);
        }

        // From this file's own path, so the check works wherever the test build lands.
        private static string Root([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
            if (!File.Exists(Path.Combine(root, "Shell", "MainWindow.xaml")))
                throw new InvalidOperationException("Shell/MainWindow.xaml was not found beside the test project.");
            return root;
        }
    }

    /// <summary>Window confirmation of the wiring above, run on the lab VM: open a day with an
    /// encrypted Killendar shown, press Ctrl+L, and its rows leave the list.</summary>
    [Collection("Calendar settings")]
    public sealed class ShownCalendarsAgendaRegressionTests
    {
        [Fact]
        public void LockingAnEncryptedShownCalendarClearsItsRowsFromTheDayList()
            => ScratchUiProcess.Run(typeof(ShownCalendarsAgendaFixture));
    }

    public sealed class ShownCalendarsAgendaFixture
    {
        private static readonly DateTime Day = new(2026, 10, 20);
        private const string SecretTitle = "Dentist behind the lock";

        public void Run()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
                    Exercise();
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(60000)) throw new TimeoutException("The shown calendar agenda fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise()
        {
            using var profile = new AgendaProfile();
            var secret = new EventStore();
            secret.Open(Path.Combine(profile.Calendars, "Secret.kcal"));
            secret.Add(new CalendarEvent { Title = SecretTitle, Start = Day.AddHours(9), End = Day.AddHours(10) });
            secret.SetPassword("fixture-password");
            secret.Close();

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

                ShownCalendars.Reload("fixture.kcal");
                var item = ShownCalendars.Items.Single(i => i.Name == "Secret.kcal");
                Assert.True(ShownCalendars.Unlock(item, "fixture-password"), "The fixture calendar did not unlock.");

                Invoke(window, "ShowDayAgenda", Day, null);
                var list = (Panel)window.FindName("AgendaListHost");
                Assert.Contains(Texts(list), t => t.Contains(SecretTitle));

                Invoke(window, "LockShortcut");   // what Ctrl+L runs (Shortcuts.cs)

                Assert.Equal(ShownCalendars.State.Locked, item.State);
                Assert.DoesNotContain(Texts(list), t => t.Contains(SecretTitle));
            }
            finally
            {
                ShownCalendars.CloseAll();
                app.Shutdown();
            }
        }

        private static void Invoke(MainWindow window, string method, params object?[] args)
            => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

        private static List<string> Texts(DependencyObject root)
        {
            var texts = new List<string>();
            void Walk(DependencyObject node)
            {
                if (node is TextBlock block && !string.IsNullOrEmpty(block.Text)) texts.Add(block.Text);
                string name = AutomationProperties.GetName(node);
                if (!string.IsNullOrEmpty(name)) texts.Add(name);
                foreach (object child in LogicalTreeHelper.GetChildren(node))
                    if (child is DependencyObject d) Walk(d);
            }
            Walk(root);
            return texts;
        }

        private sealed class AgendaProfile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\shown-agenda", Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly object? _oldDir, _oldPath, _oldCache;
            public string Calendars => Path.Combine(_root, "calendars");
            public AgendaProfile()
            {
                Directory.CreateDirectory(Calendars);
                _oldDir = _dir.GetValue(null); _oldPath = _path.GetValue(null); _oldCache = _cache.GetValue(null);
                _dir.SetValue(null, _root);
                _path.SetValue(null, Path.Combine(_root, "settings.json"));
                _cache.SetValue(null, new Dictionary<string, string>
                {
                    ["KillendarDataDir"] = Calendars,
                    ["ActiveKillendar"] = Path.Combine(Calendars, "fixture.kcal"),
                    ["ShownCalendars"] = "Secret.kcal|#8E6CC9",
                    ["CheckForUpdatesOnStartup"] = "0",
                    ["CloseToTray"] = "0"
                });
            }
            public void Dispose()
            {
                _dir.SetValue(null, _oldDir); _path.SetValue(null, _oldPath); _cache.SetValue(null, _oldCache);
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(_root, true); } catch (IOException) { }
            }
        }
    }
}
