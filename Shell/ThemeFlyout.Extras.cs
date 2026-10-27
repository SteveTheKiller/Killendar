using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Killendar.Controls;
using Killendar.Services;

namespace Killendar.Shell
{
    /// <summary>
    /// Killendar's settings for the kit theme picker (Shell/ThemeFlyout.cs): a theme switch
    /// repaints at once with no fade, an accent switch reveals the new accent under a snapshot
    /// crossfade, and the window corners follow the theme (square on 98SE).
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Call from the constructor right after InitThemePicker.</summary>
        private void ConfigureThemePicker()
        {
            // A palette tester, not a one-shot command menu: it stays open while themes are tried.
            ThemeMenu.StaysOpen = true;
            _themePicker.ThemeChangeTransition = apply => apply();
            _themePicker.AccentChangeTransition = CrossfadeAccent;
            _themePicker.ThemeApplied = () =>
                DwmChrome.SetRoundedCorners(this, rounded: ThemeManager.Current != Theme.SE98
                                                           && WindowState == WindowState.Normal);
        }

        /// <summary>Hide the synchronous DynamicResource repaint beneath a snapshot and reveal
        /// the new accent as one composed frame. This matters most in 98SE, where title, frame,
        /// selection, calendar bands and controls all change together.</summary>
        private void CrossfadeAccent(Action apply)
        {
            if (Content is not Panel root || root.ActualWidth <= 0 || root.ActualHeight <= 0)
            {
                apply();
                return;
            }

            Image? ghost = null;
            try
            {
                var bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                bitmap.Freeze();
                ghost = new Image { Source = bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false };
                Panel.SetZIndex(ghost, 10000);
                if (root is Grid grid)
                {
                    Grid.SetRowSpan(ghost, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(ghost, Math.Max(1, grid.ColumnDefinitions.Count));
                }
                root.Children.Add(ghost);
            }
            catch { ghost = null; }

            apply();
            if (ghost == null) return;
            Image held = ghost;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() =>
                {
                    var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(190))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    fade.Completed += (_, _) => root.Children.Remove(held);
                    held.BeginAnimation(UIElement.OpacityProperty, fade);
                }));
        }
    }
}
