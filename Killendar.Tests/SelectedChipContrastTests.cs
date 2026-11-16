using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class SelectedChipContrastTests
    {
        [Fact]
        public void SelectedRepeatChipTextReadsOnTheApprovedRedFill()
            => ScratchUiProcess.Run(typeof(SelectedChipContrastFixture));

        [ChipWindowFact]
        public void SelectedRepeatChipUpdatesAcrossLiveThemesAndAccents()
            => ScratchUiProcess.Run(typeof(SelectedChipContrastFixture), "matrix");
    }

    public sealed class ChipWindowFactAttribute : FactAttribute
    {
        public ChipWindowFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("KILLENDAR_WINDOW_TESTS") != "1")
                Skip = "Requires the owned lab desktop for live theme changes.";
        }
    }

    public sealed class SelectedChipContrastFixture
    {
        public void Run() => Run("red");

        public void Run(string scenario)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///chip.html"));
                    Check(scenario);
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The chip contrast check exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private static void Check(string scenario)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Window? window = null;
            try
            {
                foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Controls/AppStyles.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/Killendar;component/" + resource, UriKind.Relative),
                    });
                AppTheme.Configure();
                ThemeManager.Apply(Theme.Dark);
                ThemeManager.ApplyAccent(Theme.Dark, Accent.Red);
                var chip = new ToggleButton { Content = "Sa", IsChecked = true, Style = (Style)app.FindResource("ChipToggle") };
                var root = new Border { Child = chip, Width = 60, Height = 30 };
                using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Killendar chip contrast fixture")
                    { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
                source.RootVisual = root;
                Pump();
                root.Measure(new Size(60, 30));
                root.Arrange(new Rect(0, 0, 60, 30));
                root.UpdateLayout();
                var label = Descendants(chip).OfType<TextBlock>().Single(t => t.Text == "Sa");
                var foreground = Assert.IsType<SolidColorBrush>(label.Foreground).Color;
                // Measured midpoint of the approved Dark/Red chip fill in the release captures.
                double ratio = Contrast(foreground, Color.FromRgb(0xD7, 0x4B, 0x45));
                Assert.True(ratio >= 4.5, "Selected chip contrast is " + ratio.ToString("0.00") + ":1.");
                if (scenario == "matrix")
                {
                    source.RootVisual = null;
                    window = new Window
                    {
                        Content = root, Width = 100, Height = 80, ShowActivated = false, ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
                    };
                    window.Show();
                    Pump();
                    int checkedStates = 0;
                    foreach (Theme theme in Enum.GetValues(typeof(Theme)))
                    {
                        ThemeManager.Apply(theme);
                        var accents = ThemeManager.SupportsAccents(theme)
                            ? (Accent[])Enum.GetValues(typeof(Accent)) : new[] { ThemeManager.AccentChoiceFor(theme) };
                        foreach (var accent in accents)
                        {
                            if (ThemeManager.SupportsAccents(theme)) ThemeManager.ApplyAccent(theme, accent);
                            Pump();
                            root.UpdateLayout();
                            var face = (Border)chip.Template.FindName("border", chip);
                            var fill = (Brush)app.FindResource("PrimaryFillBrush");
                            Assert.Equal(Describe(fill), Describe(face.Background));
                            var average = fill is SolidColorBrush flat ? flat.Color : Average((GradientBrush)fill);
                            var preferred = ((SolidColorBrush)app.FindResource("OnPrimaryBrush")).Color;
                            var ink = ((SolidColorBrush)label.Foreground).Color;
                            Assert.True(Contrast(ink, average) >= 4.5, theme + "/" + accent + " selected text needs more contrast.");
                            if (Contrast(preferred, average) >= 4.5) Assert.Equal(preferred, ink);
                            Assert.Equal(60, root.ActualWidth);
                            Assert.Equal(30, root.ActualHeight);
                            chip.IsChecked = false;
                            root.UpdateLayout();
                            Assert.Equal(((SolidColorBrush)app.FindResource("MutedTextBrush")).Color,
                                ((SolidColorBrush)label.Foreground).Color);
                            chip.IsChecked = true;
                            root.UpdateLayout();
                            checkedStates++;
                        }
                    }
                    Assert.Equal(41, checkedStates);
                }
            }
            finally { window?.Close(); app.Shutdown(); }
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }

        private static Color Average(GradientBrush brush) => Color.FromRgb(
            (byte)brush.GradientStops.Average(s => s.Color.R), (byte)brush.GradientStops.Average(s => s.Color.G),
            (byte)brush.GradientStops.Average(s => s.Color.B));

        private static string Describe(Brush brush) => brush is SolidColorBrush solid
            ? solid.Color.ToString() + "/" + solid.Opacity
            : string.Join(";", ((GradientBrush)brush).GradientStops.Select(s => s.Offset + ":" + s.Color)) + "/" + brush.Opacity;

        private static void Pump()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static double Contrast(Color first, Color second)
        {
            double Luminance(Color color)
            {
                double Channel(byte value)
                {
                    double c = value / 255.0;
                    return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
                }
                return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
            }
            double a = Luminance(first), b = Luminance(second);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }
    }
}
