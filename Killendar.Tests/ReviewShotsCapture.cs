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
using Killendar.Models;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    // Review pictures for a person to look at, drawn off screen with RenderTargetBitmap.
    // Does nothing unless KILLENDAR_WINDOW_TESTS=1 and KILLENDAR_SHOTS_DIR names a folder for the
    // .png files. Each picture is drawn in its own scratch process, since one process holds one
    // Application and one theme.
    [Collection("Calendar settings")]
    public sealed class ReviewShotsCapture
    {
        [Fact]
        public void DrawTheReviewShots()
        {
            if (Environment.GetEnvironmentVariable("KILLENDAR_WINDOW_TESTS") != "1") return;
            string? folder = Environment.GetEnvironmentVariable("KILLENDAR_SHOTS_DIR");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            foreach (string theme in new[] { "Dark", "SE98" })
                ScratchUiProcess.Run(typeof(ReviewShotsFixture), "undo|" + theme + "|" + folder);
            foreach (string theme in new[] { "Dark", "Light", "SE98" })
                ScratchUiProcess.Run(typeof(ReviewShotsFixture), "gi007|" + theme + "|" + folder);
        }
    }

    public sealed class ReviewShotsFixture
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
                    var parts = scenario.Split('|');
                    Exercise(parts[0], parts[1], parts[2]);
                }
                catch (Exception ex) { failure = ex; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(90000)) throw new TimeoutException("The review shots fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private static string FileTag(string theme) => theme == "SE98" ? "98se" : theme.ToLowerInvariant();

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise(string kind, string theme, string folder)
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
            ThemeManager.Apply((Theme)Enum.Parse(typeof(Theme), theme));
            try
            {
                string tag = FileTag(theme);
                if (kind == "undo") FooterUndo(profile, Path.Combine(folder, "footer-undo-" + tag + ".png"));
                else if (kind == "gi007") SelectionStyles(Path.Combine(folder, "gi007-" + tag + ".png"));
                else throw new ArgumentException(kind);
            }
            finally { app.Shutdown(); }
        }

        // The status bar right after a template is deleted: the Undo button beside the message.
        private static void FooterUndo(Profile profile, string path)
        {
            var window = new MainWindow();
            try
            {
                const double width = 1400, height = 900;
                // Shown, as the snapshot fixture does, but far off every screen (and on the hidden
                // desktop), so it lays out as it does for real.
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.WindowState = WindowState.Normal;
                window.Left = -30000;
                window.Top = -30000;
                window.Width = width;
                window.Height = height;
                window.Show();
                window.Left = -30000;
                window.Top = -30000;
                for (int i = 0; i < 5; i++) Pump();
                var store = (EventStore)typeof(MainWindow).GetField("_store", Instance)!.GetValue(window)!;
                store.SaveTemplate(new CalendarEvent
                {
                    Title = "Weekly review", Location = "Desk", Description = "Notes",
                    Start = new DateTime(2026, 11, 10, 9, 0, 0), End = new DateTime(2026, 11, 10, 10, 30, 0),
                });
                var root = (FrameworkElement)window.Content;

                var menu = KillerFamily.Controls.CardBuilder.Menu(280);
                typeof(MainWindow).GetMethod("AddTemplateItems", Instance)!.Invoke(window, new object[] { menu, new DateTime(2026, 11, 12) });
                var delete = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Delete template");
                var item = delete.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Weekly review");
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                for (int i = 0; i < 10; i++) { Pump(); Thread.Sleep(50); }
                root.UpdateLayout();
                var undo = (Button)window.FindName("StatusUndoBtn")!;
                Assert.Equal(Visibility.Visible, undo.Visibility);
                Write(root, window.Background ?? Brushes.Black, root.ActualWidth, root.ActualHeight, path, 120);
            }
            finally { window.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }
        }

        // GI-007: one selected row of each selection style, side by side. PickerRow marks the
        // selection with PrimaryFillBrush and OnPrimaryBrush; the other three use SelectionBg and
        // SelectionFg.
        private static void SelectionStyles(string path)
        {
            Style S(string key) => (Style)Application.Current.FindResource(key);
            Brush B(string key) => (Brush)Application.Current.FindResource(key);

            FrameworkElement Column(string label, string brushes, FrameworkElement sample)
            {
                var stack = new StackPanel { Width = 230, Margin = new Thickness(10) };
                stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Bold, Foreground = B("TextBrush"), Margin = new Thickness(0, 0, 0, 2) });
                stack.Children.Add(new TextBlock { Text = brushes, FontSize = 10, Foreground = B("DimTextBrush"), Margin = new Thickness(0, 0, 0, 8) });
                stack.Children.Add(sample);
                return stack;
            }

            ListBox List(string styleKey)
            {
                var box = new ListBox
                {
                    ItemContainerStyle = S(styleKey),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                };
                box.Items.Add("Documents");
                box.Items.Add("Pictures");
                box.SelectedIndex = 0;
                return box;
            }

            var tree = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), ItemContainerStyle = S("FolderTreeItem") };
            var node = new TreeViewItem { Header = "Documents", IsSelected = true };
            tree.Items.Add(node);
            tree.Items.Add(new TreeViewItem { Header = "Pictures" });

            var combo = new StackPanel();
            combo.Children.Add(new ComboBoxItem { Content = "Documents", IsSelected = true, Style = S("DarkComboItem") });
            combo.Children.Add(new ComboBoxItem { Content = "Pictures", Style = S("DarkComboItem") });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Column("PickerRow", "PrimaryFillBrush / OnPrimaryBrush", List("PickerRow")));
            row.Children.Add(Column("PickerPlaceRow", "SelectionBg / SelectionFg", List("PickerPlaceRow")));
            row.Children.Add(Column("FolderTreeItem", "SelectionBg / SelectionFg", tree));
            row.Children.Add(Column("DarkComboItem", "SelectionBg / SelectionFg", combo));

            var surface = new Border { Background = B("BackgroundBrush"), Padding = new Thickness(10), Child = row };
            System.Windows.Documents.TextElement.SetForeground(surface, B("TextBrush"));
            surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = surface.DesiredSize;
            surface.Arrange(new Rect(size));
            surface.UpdateLayout();
            Pump();
            Write(surface, B("BackgroundBrush"), Math.Ceiling(size.Width), Math.Ceiling(size.Height), path, 0);
        }

        private static void Pump()
        {
            for (int i = 0; i < 5; i++)
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        // Draws the element on its background, then keeps only the bottom cropBottom pixels
        // (all of it when 0).
        private static void Write(FrameworkElement element, Brush background, double width, double height, string path, int cropBottom)
        {
            var back = new DrawingVisual();
            using (var dc = back.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(back);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            bitmap.Render(element);
            BitmapSource result = bitmap;
            if (cropBottom > 0 && cropBottom < (int)height)
                result = new CroppedBitmap(bitmap, new Int32Rect(0, (int)height - cropBottom, (int)width, cropBottom));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));
            using var file = File.Create(path);
            encoder.Save(file);
        }

        private sealed class Profile : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root + @"\review-shots", Guid.NewGuid().ToString("N"));
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
