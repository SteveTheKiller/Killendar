using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Killendar.Shell
{
    /// <summary>
    /// APP-OWNED: Killendar's side of the family app size (Shell/AppScale.cs). Killendar keeps its
    /// 80 to 150% range, ClearType text at every size, layout rounding only at 100%, a minimum
    /// window size that follows the scale, the "UI  120%" readout, and no wheel while the sidebar
    /// slides.
    /// </summary>
    public partial class MainWindow
    {
        private const double BaseContentMinHeight = 400;
        private const double FixedChromeHeight = 60;   // 36 title + 24 footer

        partial void ConfigureAppScale(ref double min, ref double max, ref double step, ref bool smoothText)
        {
            min = 0.80;
            max = 1.50;
            step = 0.02;
            smoothText = false;   // ApplyAppScaleTo sets the formatting mode itself
        }

        /// <summary>Call from the constructor right after InitAppScale.</summary>
        private void InitAppScaleExtras()
        {
            // A window-level tunnel runs before the wordmark's own wheel handler, which then skips
            // the handled event, so a wheel notch during the sidebar slide does nothing.
            PreviewMouseWheel += (_, e) =>
            {
                if (_riding && e.OriginalSource is DependencyObject d && IsInLogoBar(d)) e.Handled = true;
            };
            // No saved size: still apply 1.0 so the minimum size and text mode are set.
            if (Services.ThemeManager.GetSetting("AppScale") is null) ApplyAppScale(1.0);
        }

        private bool IsInLogoBar(DependencyObject? d)
        {
            for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (ReferenceEquals(d, LogoBar)) return true;
            return false;
        }

        partial void ApplyAppScaleTo(double scale, ref bool handled)
        {
            ScaleHost.UseLayoutRounding = scale == 1.0;
            TextOptions.SetTextFormattingMode(ScaleHost,
                scale == 1.0 ? TextFormattingMode.Display : TextFormattingMode.Ideal);

            MinWidth = Math.Min(_sidebarOpen ? OpenMinWidth : ClosedMinWidth, SystemParameters.WorkArea.Width);
            MinHeight = Math.Min(FixedChromeHeight + BaseContentMinHeight * scale, SystemParameters.WorkArea.Height);

            if (WindowState == WindowState.Normal)
            {
                double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
                double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
                Width = Math.Max(currentWidth, MinWidth);
                Height = Math.Max(currentHeight, MinHeight);
                // Keep the grown window inside its own monitor's work area. Before the HWND exists
                // (the constructor restoring the saved scale) the SourceInitialized fit does this.
                if (PresentationSource.FromVisual(this) != null)
                    KillerFamily.Windowing.WorkAreaFit.Fit(this, keepMargin: false);
            }
        }

        private DispatcherTimer? _scaleReadoutHide;
        private string? _statusBeforeScale;

        partial void ShowAppScaleReadout(double scale, ref bool handled)
        {
            handled = true;
            if (_scaleReadoutHide == null)
            {
                _scaleReadoutHide = new DispatcherTimer(DispatcherPriority.Normal)
                    { Interval = TimeSpan.FromSeconds(5) };
                _scaleReadoutHide.Tick += (_, _) =>
                {
                    _scaleReadoutHide.Stop();
                    StatusText.Text = _statusBeforeScale ?? Loc("Str_Status_Ready");
                    _statusBeforeScale = null;
                };
            }

            if (!_scaleReadoutHide.IsEnabled) _statusBeforeScale = StatusText.Text;
            _scaleReadoutHide.Stop();
            StatusText.Text = $"UI  {(int)Math.Round(scale * 100)}%";
            _scaleReadoutHide.Start();
        }
    }
}
