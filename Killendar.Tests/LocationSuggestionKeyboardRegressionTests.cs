using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class LocationSuggestionKeyboardRegressionTests
    {
        [Fact]
        public void LocationSuggestionsSupportKeyboardAndRespectCancellation()
            => ScratchUiProcess.Run(typeof(LocationSuggestionFixture));
    }

    public sealed class LocationSuggestionFixture
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
            if (!thread.Join(60000)) throw new TimeoutException("The location fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise()
        {
            using var profile = new LocationProfile();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            ThemeManager.GetSetting = Settings.Get;
            ThemeManager.SetSetting = Settings.Set;
            using var source = new HwndSource(new HwndSourceParameters("Killendar location keyboard fixture")
                { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
            var window = new MainWindow();
            var content = (FrameworkElement)window.Content;
            source.RootVisual = content;
            content.Measure(new Size(1200, 800));
            content.Arrange(new Rect(0, 0, 1200, 800));
            content.UpdateLayout();
            var popup = (Popup)window.FindName("LocationSuggestionsPopup");
            popup.StaysOpen = true;
            try
            {
                int calls = 0;
                window.PhotonRequestAsync = (url, cancellation) =>
                {
                    calls++;
                    return Task.FromResult("{\"features\":[{\"properties\":{\"name\":\"First Avenue\"}},{\"properties\":{\"name\":\"Second Avenue\"}}]}");
                };
                ((Button)window.FindName("NewEventBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var field = (TextBox)window.FindName("FieldLocation");
                var list = (ListBox)window.FindName("LocationSuggestionsList");
                field.Text = "search address";
                PumpUntil(() => list.Items.Count == 2);
                Assert.Equal(1, calls);
                Assert.True(popup.IsOpen, "The fake results must open the real suggestion popup before keyboard input.");

                Press(field, source, Key.Down);

                Assert.Equal(0, list.SelectedIndex);
                Assert.Equal("search address", field.Text);
                Press(list, source, Key.Enter);
                Assert.Equal("First Avenue", field.Text);
                Assert.False(popup.IsOpen, "Accepting a suggestion must dismiss the popup.");

                var delayed = new TaskCompletionSource<string>();
                CancellationToken pendingCancellation = default;
                bool requestStarted = false;
                window.PhotonRequestAsync = (url, cancellation) =>
                {
                    pendingCancellation = cancellation;
                    requestStarted = true;
                    return delayed.Task;
                };
                field.Text = "pending address";
                PumpUntil(() => requestStarted);
                ((Button)window.FindName("SidebarToggleBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                bool cancelledOnClose = pendingCancellation.IsCancellationRequested;
                delayed.SetResult("{\"features\":[{\"properties\":{\"name\":\"Late Avenue\"}}]}");
                PumpFor(200);
                Assert.False(popup.IsOpen, "A late response must not reopen suggestions after the editor closes.");
                Assert.True(cancelledOnClose, "Closing the editor must cancel its active lookup request.");
                Assert.Empty(list.Items);

                ((Button)window.FindName("NewEventBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var title = (TextBox)window.FindName("FieldTitle");
                title.Text = "Unsaved location editor";
                var toggle = (CheckBox)window.FindName("PhotonLookupToggle");
                toggle.IsChecked = false;
                int offCalls = 0;
                const string results = "{\"features\":[{\"properties\":{\"name\":\"First Avenue\"}},{\"properties\":{\"name\":\"Second Avenue\"}}]}";
                window.PhotonRequestAsync = (url, cancellation) => { offCalls++; return Task.FromResult(results); };
                field.Text = "disabled lookup";
                PumpFor(800);
                Assert.Equal(0, offCalls);
                Assert.False(popup.IsOpen, "Opted-out lookup must not open suggestions.");
                toggle.IsChecked = true;
                Action<string> show = query =>
                {
                    field.Text = query;
                    PumpUntil(() => popup.IsOpen && list.Items.Count == 2);
                };

                show("keyboard enter");
                Press(field, source, Key.Down);
                Press(list, source, Key.Down);
                Assert.Equal(1, list.SelectedIndex);
                Press(list, source, Key.Up);
                Assert.Equal(0, list.SelectedIndex);
                Press(list, source, Key.Enter);
                Assert.Equal("First Avenue", field.Text);
                Assert.False(popup.IsOpen, "Enter must accept and dismiss suggestions.");

                show("keyboard space");
                Press(field, source, Key.Up);
                Assert.Equal(1, list.SelectedIndex);
                Press(list, source, Key.Space);
                Assert.Equal("Second Avenue", field.Text);
                Assert.False(popup.IsOpen, "Space on the list must accept its selected suggestion.");

                show("keyboard escape");
                Assert.True(Press(field, source, Key.Escape).Handled);
                Assert.False(popup.IsOpen, "Escape must dismiss suggestions.");
                Assert.True(((Grid)window.FindName("SidebarContent")).IsEnabled,
                    "Escape from suggestions must not close the appointment editor.");
                Assert.Equal("Unsaved location editor", title.Text);

                show("keyboard field tab");
                Assert.False(Press(field, source, Key.Tab).Handled,
                    "Tab in the location field must retain normal focus traversal.");
                Assert.False(popup.IsOpen, "Tab must dismiss suggestions.");
                show("keyboard list tab");
                Press(field, source, Key.Down);
                Assert.True(Press(list, source, Key.Tab).Handled,
                    "Tab from the popup must transfer focus back through the editor.");
                Assert.False(popup.IsOpen, "Tab from the popup must dismiss suggestions.");

                var oldFailure = new TaskCompletionSource<string>();
                bool oldStarted = false;
                window.PhotonRequestAsync = (url, cancellation) => { oldStarted = true; return oldFailure.Task; };
                field.Text = "old failing request";
                PumpUntil(() => oldStarted);
                window.PhotonRequestAsync = (url, cancellation) => Task.FromResult(results);
                show("new surviving response");
                oldFailure.SetException(new IOException("Fixture lookup failed."));
                PumpFor(150);
                Assert.True(popup.IsOpen, "A stale failure must not dismiss newer suggestions.");
                Assert.Equal(2, list.Items.Count);

                var shortRequest = new TaskCompletionSource<string>();
                bool shortStarted = false;
                CancellationToken shortCancellation = default;
                window.PhotonRequestAsync = (url, cancellation) =>
                {
                    shortCancellation = cancellation;
                    shortStarted = true;
                    return shortRequest.Task;
                };
                field.Text = "pending short query";
                PumpUntil(() => shortStarted);
                field.Text = "ab";
                Assert.True(shortCancellation.IsCancellationRequested, "Shortening input must cancel its lookup.");
                shortRequest.SetResult(results);
                PumpFor(150);
                Assert.False(popup.IsOpen, "A shortened query must not accept a late response.");
                Assert.Empty(list.Items);
                Assert.False(window.IsLoaded);
                Assert.Empty(Directory.GetFiles(profile.Calendars));
            }
            finally { popup.IsOpen = false; source.RootVisual = null; app.Shutdown(); }
        }

        private static void PumpFor(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
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

        private static void PumpUntil(Func<bool> condition)
        {
            var elapsed = Stopwatch.StartNew();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (condition() || elapsed.ElapsedMilliseconds >= 10000) { timer.Stop(); frame.Continue = false; }
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Assert.True(condition(), "The fake location response did not reach the real controls before its deadline.");
        }

        private sealed class LocationProfile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\location",
                Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly object? _oldDir, _oldPath, _oldCache;
            public string Calendars => Path.Combine(_root, "calendars");
            public LocationProfile()
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
                    ["PhotonLookup"] = "1",
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
