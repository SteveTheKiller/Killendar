using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Killendar.Controls
{
    // App-owned sidebar behavior. Masks only visible content and follows the live theme.
    internal static class SidebarEdgeFade
    {
        private static readonly DependencyProperty StrengthProperty =
            DependencyProperty.RegisterAttached("Strength", typeof(double), typeof(SidebarEdgeFade),
                new PropertyMetadata(1d, (sender, _) => Update((FrameworkElement)sender)));

        internal static void Attach(FrameworkElement host)
        {
            host.SetResourceReference(StrengthProperty, "EdgeFadeOpacity");
            host.Loaded += (_, _) => Update(host);
            host.SizeChanged += (_, _) => Update(host);
            host.IsVisibleChanged += (_, _) => Update(host);
            host.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, args) =>
                {
                    var scroll = host as ScrollViewer ?? Find<ScrollViewer>(host);
                    if (ReferenceEquals(args.OriginalSource, scroll)) Update(host);
                }));
        }

        private static void Update(FrameworkElement host)
        {
            var scroll = host as ScrollViewer ?? Find<ScrollViewer>(host);
            if (scroll?.Template?.FindName("PART_ScrollContentPresenter", scroll)
                    is not ScrollContentPresenter viewport) return;
            double strength = Math.Max(0, Math.Min(1, (double)host.GetValue(StrengthProperty)));
            bool top = scroll.VerticalOffset > 0.5;
            bool bottom = scroll.ScrollableHeight - scroll.VerticalOffset > 0.5;
            double height = viewport.ActualHeight;
            if (strength <= 0 || height <= 0 || (!top && !bottom))
            {
                viewport.OpacityMask = null;
                return;
            }

            // Relative brush bounds include off-screen rows. Absolute coordinates keep the
            // gradient on the viewport while leaving the sibling scrollbars untouched.
            double edge = Math.Min(0.45, 28 / height);
            var faded = Color.FromArgb((byte)Math.Round(255 * (1 - strength)), 0, 0, 0);
            var mask = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, height)
            };
            mask.GradientStops.Add(new GradientStop(top ? faded : Colors.Black, 0));
            mask.GradientStops.Add(new GradientStop(Colors.Black, edge));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - edge));
            mask.GradientStops.Add(new GradientStop(bottom ? faded : Colors.Black, 1));
            mask.Freeze();
            viewport.OpacityMask = mask;
        }

        private static T? Find<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T found) return found;
                if (Find<T>(child) is T nested) return nested;
            }
            return null;
        }
    }
}
