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
        {
            var domain = AppDomain.CreateDomain("Killendar picker path fixture", null,
                new AppDomainSetup { ApplicationBase = AppContext.BaseDirectory });
            try
            {
                var fixture = (PickerPathFixture)domain.CreateInstanceAndUnwrap(
                    typeof(PickerPathFixture).Assembly.FullName, typeof(PickerPathFixture).FullName);
                fixture.Run();
            }
            finally { AppDomain.Unload(domain); }
        }
    }

    public sealed class PickerPathFixture : MarshalByRefObject
    {
        public void Run()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { ExercisePickerActions(); }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(20000), "The scratch picker fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private static void ExercisePickerActions()
        {
            var app = Application.Current ?? new Application();
            var previousGet = ThemeManager.GetSetting;
            var previousSet = ThemeManager.SetSetting;
            string directory = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\picker",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "x.txt"));
            var settings = new Dictionary<string, string>();
            ThemeManager.GetSetting = key => settings.TryGetValue(key, out var value) ? value : null;
            ThemeManager.SetSetting = (key, value) => settings[key] = value;
            try
            {
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
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
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var name = (TextBox)dialog.FindName("FileNameBox");
                        var filters = (ComboBox)dialog.FindName("FilterCombo");
                        var accept = (Button)dialog.FindName("AcceptButton");
                        var metadata = (TextBlock)dialog.FindName("SelMeta");
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
                        Assert.Equal(directory, ((TextBox)dialog.FindName("PathBox")).Text);
                        Assert.Equal("*.txt", filters.Items[0]);
                        Assert.Equal("", name.Text);

                        name.Text = "\"accepted.ics\"";
                        accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.Equal(Path.Combine(directory, "accepted.ics"), dialog.FileName);
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

        private static void PressEnter(TextBox name)
            => name.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(name), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
    }
}
