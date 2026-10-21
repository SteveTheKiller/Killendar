using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class ModalShortcutRoutingRegressionTests
    {
        [Fact]
        public void ModalOverlaysPreserveEditorAndCalendarUntilDismissed()
            => ScratchUiProcess.Run(typeof(ModalShortcutFixture));
    }

    public sealed class ModalShortcutFixture : MarshalByRefObject
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
            if (!thread.Join(20000)) throw new TimeoutException("The modal shortcut fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise()
        {
            using var profile = new ModalProfile();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            ThemeManager.GetSetting = Settings.Get;
            ThemeManager.SetSetting = Settings.Set;
            using var source = new HwndSource(new HwndSourceParameters("Killendar modal keyboard fixture")
                { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
            var window = new MainWindow();
            try
            {
                Assert.False(window.IsVisible);
                ((Button)window.FindName("NewEventBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var title = (TextBox)window.FindName("FieldTitle");
                title.Text = "Unsaved appointment";
                var about = (Grid)window.FindName("AboutOverlay");
                var shortcuts = (Grid)window.FindName("ShortcutsOverlay");
                var period = (TextBlock)window.FindName("PeriodLabel");
                var calendar = (ContentControl)window.FindName("ViewHost");
                string originalPeriod = period.Text;
                object originalView = calendar.Content;
                foreach (Grid overlay in new[] { about, shortcuts })
                {
                    about.Visibility = shortcuts.Visibility = Visibility.Collapsed;
                    overlay.Visibility = Visibility.Visible;
                    foreach (Key key in new[] { Key.N, Key.T, Key.Left, Key.Right, Key.M,
                        Key.W, Key.D, Key.A, Key.R, Key.B, Key.F5 })
                    {
                        Press(window, source, key);
                        Assert.Equal("Unsaved appointment", title.Text);
                        Assert.Equal(originalPeriod, period.Text);
                        Assert.Same(originalView, calendar.Content);
                        Assert.Equal(Visibility.Visible, overlay.Visibility);
                    }
                    UIElement control = overlay == about
                        ? (UIElement)window.FindName("AboutStartupUpdateCheck")
                        : (UIElement)window.FindName("KsViewList");
                    Assert.False(Press(control, source, Key.Tab).Handled);
                    Assert.False(Press(control, source, Key.Enter).Handled);
                }
                about.Visibility = shortcuts.Visibility = Visibility.Visible;
                Assert.True(Press(window, source, Key.Escape).Handled);
                CompleteAnimations();
                Assert.Equal(Visibility.Collapsed, about.Visibility);
                Assert.Equal(Visibility.Visible, shortcuts.Visibility);
                Assert.True(Press(window, source, Key.Escape).Handled);
                CompleteAnimations();
                Assert.Equal(Visibility.Collapsed, shortcuts.Visibility);
                Assert.Equal("Unsaved appointment", title.Text);
                Assert.False(window.IsLoaded);
                // N over a draft asks first (DraftProtectionRegressionTests); with the draft
                // emptied, N reaches the editor again once the overlays are gone.
                title.Text = "";
                Assert.True(Press(window, source, Key.N).Handled);
                Assert.Equal("", title.Text);
                Assert.Empty(Directory.GetFiles(profile.Calendars));
            }
            finally { app.Shutdown(); }
        }

        private static void CompleteAnimations()
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static KeyEventArgs Press(UIElement target, PresentationSource source, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(args);
            return args;
        }

        private sealed class ModalProfile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\modal",
                Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly object? _oldDir, _oldPath, _oldCache;
            public string Calendars => Path.Combine(_root, "calendars");
            public ModalProfile()
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
