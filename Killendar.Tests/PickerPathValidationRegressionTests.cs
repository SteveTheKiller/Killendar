using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Killendar.Controls;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class PickerPathValidationRegressionTests
    {
        [Fact]
        public void InvalidPathsKeepDialogUsableThroughPickerActions()
            => ScratchUiProcess.Run(typeof(PickerPathFixture));
    }

    public sealed class PickerPathFixture : MarshalByRefObject
    {
        public void Run()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
                    ExercisePickerActions();
                }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(60000), "The scratch picker fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ExercisePickerActions()
        {
            var app = Application.Current ?? new Application();
            var previousGet = ThemeManager.GetSetting;
            var previousSet = ThemeManager.SetSetting;
            string directory = Path.Combine(TestScratch.Root + @"\picker",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "x.txt"));
            var settings = new Dictionary<string, string>();
            Killendar.Services.AppTheme.Configure();
            ThemeManager.GetSetting = key => settings.TryGetValue(key, out var value) ? value : null;
            ThemeManager.SetSetting = (key, value) => settings[key] = value;
            try
            {
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Controls/AppStyles.xaml", "Strings/en-US.xaml" })
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                        });
                }
                var dialog = new FileDialog(FileDialogMode.Save)
                {
                    InitialDirectory = directory, FileName = "original.ics",
                    Filter = "Calendar|*.ics|Text|*.txt", OverwritePrompt = false
                };
                Exception? interactionFailure = null;
                // What the dialog read and who asked it to close, so a failed first listing says why.
                var trace = new System.Text.StringBuilder();
                var started = System.Diagnostics.Stopwatch.StartNew();
                var reader = dialog.DirectoryListingReader;
                dialog.DirectoryListingReader = (path, hidden, token) =>
                {
                    try
                    {
                        var result = reader(path, hidden, token);
                        lock (trace) trace.Append(" | read '" + path + "' at " + started.ElapsedMilliseconds + " ms: exists "
                            + result.Exists + ", complete " + result.Complete + ", " + result.Entries.Count + " rows, error '" + result.Error + "'");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        lock (trace) trace.Append(" | read '" + path + "' threw " + ex.GetType().Name + ": " + ex.Message);
                        throw;
                    }
                };
                dialog.SourceInitialized += (_, _) =>
                {
                    bool seen = false;
                    System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(dialog).Handle)
                        .AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                        {
                            if (message == 0x0010 && !seen)
                            {
                                seen = true;
                                lock (trace) trace.Append(" | WM_CLOSE at " + started.ElapsedMilliseconds + " ms from " + Environment.StackTrace);
                            }
                            return IntPtr.Zero;
                        });
                };
                Func<string> state = () =>
                {
                    var type = typeof(FileDialog);
                    Func<string, object?> field = name => type.GetField(name, System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic)?.GetValue(dialog);
                    var status = ((TextBlock)dialog.FindName("SelMeta")).Text;
                    string codes = string.Join(" ", Array.ConvertAll(status.ToCharArray(), c => ((int)c).ToString("X4")));
                    lock (trace) return "visible " + dialog.IsVisible + ", closed " + field("_listingClosed") + ", fading "
                        + field("_fadingOut") + ", current '" + field("_currentDir") + "', version " + field("_navigationVersion")
                        + ", status code points [" + codes + "]" + trace;
                };
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var name = (TextBox)dialog.FindName("FileNameBox");
                        var filters = (ComboBox)dialog.FindName("FilterCombo");
                        var accept = (Button)dialog.FindName("AcceptButton");
                        var metadata = (TextBlock)dialog.FindName("SelMeta");
                        var pathBox = (TextBox)dialog.FindName("PathBox");
                        // The first listing must land before anything else is judged.
                        // The start folder is probed off the UI thread before it lists, so "not busy" alone
                        // can mean the listing has not started yet.
                        PumpUntil(() => dialog.IsDirectoryListingAvailable);
                        Assert.True(directory == pathBox.Text, "First listing: path '" + pathBox.Text + "', busy "
                            + dialog.IsDirectoryListingBusy + ", available " + dialog.IsDirectoryListingAvailable
                            + ", status '" + metadata.Text + "', " + state());
                        foreach (string invalid in new[] { "bad|name.ics", "bad\"name.ics",
                            new string('x', 300) + ".ics", @"C::\broken.ics" })
                        foreach (string route in new[] { "enter", "accept", "filter" })
                        {
                            name.Text = invalid;
                            metadata.Text = "";
                            if (route == "enter") PressEnter(name);
                            else if (route == "accept") accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            else filters.SelectedIndex = filters.SelectedIndex == 0 ? 1 : 0;
                            Assert.True(dialog.IsVisible);
                            Assert.Equal("original.ics", dialog.FileName);
                            Assert.Equal(invalid, name.Text);
                            Assert.Equal("Enter a valid file name or path.", metadata.Text);
                            Assert.Same(name, FocusManager.GetFocusedElement(dialog));
                            Assert.Empty(Directory.GetFiles(directory));
                        }

                        filters.SelectedIndex = 0;
                        name.Text = @"\\fixture-server\fixture-share\calendar.ics";
                        metadata.Text = "";
                        filters.SelectedIndex = 1;
                        Assert.Equal(@"\\fixture-server\fixture-share\calendar.txt", name.Text);
                        Assert.Equal("", metadata.Text);
                        name.Text = "*.ics";
                        PressEnter(name);
                        Assert.Equal("", name.Text);
                        Assert.Equal("*.ics", filters.Items[0]);
                        name.Text = "*.txt";
                        PressEnter(name);
                        // The picker lists and probes off the UI thread; wait for it as a person would.
                        PumpUntil(() => dialog.IsDirectoryListingAvailable && name.Text.Length == 0);
                        Assert.True(directory == pathBox.Text, "After a wildcard: path '" + pathBox.Text + "', busy "
                            + dialog.IsDirectoryListingBusy + ", available " + dialog.IsDirectoryListingAvailable
                            + ", status '" + metadata.Text + "'");
                        Assert.Equal("*.txt", filters.Items[0]);
                        Assert.Equal("", name.Text);

                        name.Text = "\"accepted.ics\"";
                        accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        PumpUntil(() => !dialog.IsVisible);
                        Assert.True(Path.Combine(directory, "accepted.ics") == dialog.FileName,
                            "File name after Accept: '" + dialog.FileName + "'");
                        Assert.False(dialog.IsVisible);
                        Assert.Empty(Directory.GetFiles(directory));
                    }
                    catch (Exception ex) { interactionFailure = ex; }
                    finally { dialog.Close(); }
                }), DispatcherPriority.ApplicationIdle);
                dialog.ShowDialog(null);
                if (interactionFailure != null) ExceptionDispatchInfo.Capture(interactionFailure).Throw();
            }
            finally
            {
                ThemeManager.GetSetting = previousGet;
                ThemeManager.SetSetting = previousSet;
                Directory.Delete(directory, true);
            }
        }

        // Runs the dispatcher until the condition holds or ten seconds pass.
        private static void PumpUntil(Func<bool> done)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!done() && clock.ElapsedMilliseconds < 10000)
            {
                var frame = new DispatcherFrame();
                // Lowest priority, so every queued picker callback runs before the frame ends.
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(10);
            }
        }

        private static void PressEnter(TextBox name)
            => name.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(name), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
    }
}
