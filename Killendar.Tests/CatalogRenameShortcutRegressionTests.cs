using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class CatalogRenameShortcutRegressionTests
    {
        [Fact]
        public void EnterInInlineRenameCommitsWithoutOpeningTheCalendar()
            => ScratchUiProcess.Run(typeof(CatalogRenameFixture), "enter");

        [Theory]
        [InlineData("delete")]
        [InlineData("backspace")]
        [InlineData("escape")]
        [InlineData("list")]
        public void TextEditingAndListShortcutsKeepTheirOwnActions(string scenario)
            => ScratchUiProcess.Run(typeof(CatalogRenameFixture), scenario);
    }

    public sealed class CatalogRenameFixture : MarshalByRefObject
    {
        public void Run(string scenario)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { Exercise(scenario); }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(20000), "The scratch catalog fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private static void Exercise(string scenario)
        {
            using var profile = new CatalogProfile();
            try { EventStore.SetDataDir(profile.CatalogDirectory); }
            catch (Exception ex)
            {
                string assemblies = string.Join("; ", AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => assembly.GetName().Name.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(assembly => assembly.FullName + " at " + assembly.Location));
                throw new InvalidOperationException("Catalog fixture SQLite assembly cohort: " + assemblies, ex);
            }
            string original = EventStore.CreateKillendar("Original");
            EventStore.SetActive(original);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            var dialog = new KillendarsDialog();
            Exception? interactionFailure = null;
            bool confirmationSeen = false;
            var dismissConfirmation = new DispatcherTimer(DispatcherPriority.Background)
                { Interval = TimeSpan.FromMilliseconds(20) };
            dismissConfirmation.Tick += (_, _) =>
            {
                foreach (var confirmation in app.Windows.OfType<ConfirmDialog>().ToArray())
                {
                    confirmationSeen = true;
                    confirmation.Close();
                }
            };
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var list = (ListBox)dialog.FindName("KcList");
                    Press(list, Key.F2);
                    var row = (StackPanel)((ListBoxItem)list.SelectedItem).Content;
                    var editor = Assert.Single(row.Children.OfType<TextBox>());
                    editor.Text = "Renamed";

                    if (scenario == "delete" || scenario == "backspace")
                    {
                        if (scenario == "delete") editor.Select(1, 1); else editor.Select(2, 0);
                        dismissConfirmation.Start();
                        Press(editor, scenario == "delete" ? Key.Delete : Key.Back);
                        Assert.False(confirmationSeen, "Editing a name requested deleting the calendar.");
                        Assert.Equal("Rnamed", editor.Text);
                        Assert.True(dialog.IsVisible);
                        Assert.Null(dialog.Selected);
                        Assert.Equal(new[] { original }, EventStore.ListKillendars());
                        Press(editor, Key.Escape);
                        Assert.Equal(original, ((ListBoxItem)list.SelectedItem).Tag);
                        return;
                    }

                    if (scenario == "escape")
                    {
                        Press(editor, Key.Escape);
                        Assert.True(dialog.IsVisible);
                        Assert.Null(dialog.Selected);
                        Assert.Equal(original, ((ListBoxItem)list.SelectedItem).Tag);
                        Assert.Equal(new[] { original }, EventStore.ListKillendars());
                        Assert.Empty(((StackPanel)((ListBoxItem)list.SelectedItem).Content).Children.OfType<TextBox>());
                        return;
                    }

                    Press(editor, Key.Enter);

                    Assert.True(dialog.IsVisible, "Enter closed the catalog while inline rename was active. Selected=" + dialog.Selected);
                    Assert.Null(dialog.Selected);
                    Assert.Equal("Renamed.kcal", ((ListBoxItem)list.SelectedItem).Tag);
                    Assert.True(File.Exists(Path.Combine(profile.CatalogDirectory, "Renamed.kcal")), "Enter left the catalog open but did not create the renamed calendar file.");
                    Assert.False(File.Exists(Path.Combine(profile.CatalogDirectory, original)));
                    Assert.Equal("Renamed.kcal", EventStore.ActiveFile);

                    if (scenario == "list")
                    {
                        list.Focus();
                        dismissConfirmation.Start();
                        Press(list, Key.Delete);
                        Assert.True(confirmationSeen, "List-level Delete did not request confirmation.");
                        Assert.True(File.Exists(Path.Combine(profile.CatalogDirectory, "Renamed.kcal")));
                        list.Focus();
                        Press(list, Key.F2);
                        var renamedRow = (StackPanel)((ListBoxItem)list.SelectedItem).Content;
                        var secondEditor = Assert.Single(renamedRow.Children.OfType<TextBox>());
                        Assert.Equal("Renamed.kcal", secondEditor.Text);
                        Press(secondEditor, Key.Escape);
                        list.Focus();
                        Press(list, Key.Enter);
                        Assert.False(dialog.IsVisible);
                        Assert.Equal("Renamed.kcal", dialog.Selected);
                    }
                }
                catch (Exception ex) { interactionFailure = ex; }
                finally { dismissConfirmation.Stop(); dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            dialog.ShowDialog();
            if (interactionFailure != null) ExceptionDispatchInfo.Capture(interactionFailure).Throw();
        }

        private static KeyEventArgs Press(UIElement target, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(args);
            if (!args.Handled)
            {
                args.RoutedEvent = Keyboard.KeyDownEvent;
                target.RaiseEvent(args);
            }
            return args;
        }

        private sealed class CatalogProfile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\catalog", Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly object? _oldDir, _oldPath, _oldCache;
            public string CatalogDirectory => Path.Combine(_root, "calendars");

            public CatalogProfile()
            {
                Directory.CreateDirectory(_root);
                _oldDir = _dir.GetValue(null); _oldPath = _path.GetValue(null); _oldCache = _cache.GetValue(null);
                _dir.SetValue(null, _root);
                _path.SetValue(null, Path.Combine(_root, "settings.json"));
                _cache.SetValue(null, null);
            }

            public void Dispose()
            {
                _dir.SetValue(null, _oldDir); _path.SetValue(null, _oldPath); _cache.SetValue(null, _oldCache);
                Directory.Delete(_root, true);
            }
        }
    }
}
