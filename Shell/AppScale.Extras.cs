using System;
using System.Windows;
using System.Windows.Input;

namespace Killendar.Shell
{
    /// <summary>
    /// APP-OWNED: Killendar's additions to the family app scale (Shell/AppScale.cs). The window's
    /// minimum size follows the scale, so the scaled calendar always fits, and the wheel is ignored
    /// while the sidebar is sliding.
    /// </summary>
    public partial class MainWindow
    {
        private const double BaseContentMinHeight = 400;
        private const double FixedChromeHeight = 60;   // 36 title + 24 footer

        /// <summary>Call from the constructor right after InitAppScale.</summary>
        private void InitAppScaleExtras()
        {
            // Window-level tunnel runs before the wordmark's own wheel handler, which then skips the
            // handled event, so a wheel notch during the sidebar slide does nothing.
            PreviewMouseWheel += (_, e) =>
            {
                if (_riding && e.OriginalSource is DependencyObject d && IsInWordmark(d)) e.Handled = true;
            };
            // No saved size: still apply 1.0 so the minimum size is set.
            if (Services.ThemeManager.GetSetting("AppScale") is null) ApplyAppScale(1.0);
        }

        /// <summary>Inside either title wordmark (LogoBar, or the plain title PlainTitleIcon sits in).</summary>
        private bool IsInWordmark(DependencyObject? d)
        {
            var plain = PlainTitleIcon.Parent;
            for (; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
                if (ReferenceEquals(d, LogoBar) || ReferenceEquals(d, plain)) return true;
            return false;
        }

        partial void ApplyAppScaleTo(double scale, ref bool handled)
        {
            ScaleHost.UseLayoutRounding = scale == 1.0;

            MinWidth = Math.Min(_sidebarOpen ? OpenMinWidth : ClosedMinWidth, SystemParameters.WorkArea.Width);
            MinHeight = Math.Min(FixedChromeHeight + BaseContentMinHeight * scale, SystemParameters.WorkArea.Height);

            if (WindowState == WindowState.Normal)
            {
                double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
                double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
                Width = Math.Max(currentWidth, MinWidth);
                Height = Math.Max(currentHeight, MinHeight);
                // Keep the grown window inside its own monitor's work area. Before the HWND exists
                // the SourceInitialized fit does this instead.
                if (PresentationSource.FromVisual(this) != null)
                    KillerFamily.Windowing.WorkAreaFit.Fit(this, keepMargin: false);
            }
        }
    }
}
