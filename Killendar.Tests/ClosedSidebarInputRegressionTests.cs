using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class ClosedSidebarInputRegressionTests
    {
        [Fact]
        public void ClosedSidebarDisablesEditorInputAndOpeningRestoresIt()
            => ScratchUiProcess.Run(typeof(ClosedSidebarInputFixture));
    }

    public sealed class ClosedSidebarInputFixture
    {
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
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(20000)) throw new TimeoutException("The sidebar fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise()
        {
            using var profile = new SidebarProfile();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            ThemeManager.GetSetting = Settings.Get;
            ThemeManager.SetSetting = Settings.Set;
            var window = new MainWindow();
            try
            {
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1200, 800));
                root.Arrange(new Rect(0, 0, 1200, 800));
                root.UpdateLayout();
                var content = (Grid)window.FindName("SidebarContent");
                var title = (TextBox)window.FindName("FieldTitle");
                Assert.False(content.IsEnabled, "The cold-start closed sidebar must reject input.");
                Assert.False(title.IsEnabled, "The cold-start title field must inherit disabled input.");
                ((Button)window.FindName("NewEventBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(content.IsEnabled, "New appointment must enable sidebar content.");
                Assert.True(title.IsEnabled, "New appointment must enable the title field.");
                title.Text = "Temporary editor";
                ((Button)window.FindName("SidebarToggleBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(content.IsEnabled, "Closing the sidebar must disable its content.");
                Assert.False(title.IsEnabled, "The closed title field must inherit disabled input.");
                ((Button)window.FindName("SidebarToggleBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(content.IsEnabled, "Reopening must enable sidebar content.");
                Assert.True(title.IsEnabled, "Reopening must enable the title field.");
                Assert.False(window.IsLoaded, "The fixture must not run the window Loaded path.");
                Assert.Empty(Directory.GetFiles(profile.Calendars));
            }
            finally { app.Shutdown(); }
        }

        private sealed class SidebarProfile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\sidebar",
                Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly object? _oldDir, _oldPath, _oldCache;
            public string Calendars => Path.Combine(_root, "calendars");
            public SidebarProfile()
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
                Directory.Delete(_root, true);
            }
        }
    }
}
