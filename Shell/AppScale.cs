using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Killendar.Shell
{
    /// <summary>App-wide accessibility sizing driven from the fixed title-bar wordmark.</summary>
    public partial class MainWindow
    {
        private const double AppScaleMin = 0.80;
        private const double AppScaleMax = 1.50;
        private const double AppScaleStep = 0.02;
        private const double BaseContentMinHeight = 400;
        private const double FixedChromeHeight = 60; // 36 title + 24 footer

        private double _appScale = 1.0;
        private DispatcherTimer? _appScaleHide;
        private string? _statusBeforeScale;

        private void InitAppScale()
        {
            if (double.TryParse(Settings.Get("AppScale"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double saved))
                ApplyAppScale(saved);
            else
                ApplyAppScale(1.0);
        }

        private void LogoBar_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_riding) return;
            ApplyAppScale(_appScale + (e.Delta > 0 ? AppScaleStep : -AppScaleStep), persist: true);
            e.Handled = true;
        }

        // The wordmark opts into client hit testing so it can receive the wheel. Restore native
        // caption drag and double-click maximize behavior explicitly.
        private void LogoBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, TitleKillendarLabel)) return;
            if (e.ClickCount == 2)
            {
                MaximizeBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed && WindowState != WindowState.Maximized)
                DragMove();
        }

        private void ApplyAppScale(double scale, bool persist = false)
        {
            scale = Math.Round(Math.Max(AppScaleMin, Math.Min(AppScaleMax, scale)), 2);
            _appScale = scale;

            ScaleHost.LayoutTransform = scale == 1.0
                ? Transform.Identity
                : new ScaleTransform(scale, scale);
            ScaleHost.UseLayoutRounding = scale == 1.0;
            TextOptions.SetTextFormattingMode(ScaleHost,
                scale == 1.0 ? TextFormattingMode.Display : TextFormattingMode.Ideal);

            MinWidth = Math.Min(_sidebarOpen ? OpenMinWidth : ClosedMinWidth,
                                SystemParameters.WorkArea.Width);
            MinHeight = Math.Min(FixedChromeHeight + BaseContentMinHeight * scale,
                                 SystemParameters.WorkArea.Height);

            if (WindowState == WindowState.Normal)
            {
                double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
                double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
                Width = Math.Max(currentWidth, MinWidth);
                Height = Math.Max(currentHeight, MinHeight);
                Left = Math.Max(SystemParameters.WorkArea.Left,
                                Math.Min(Left, SystemParameters.WorkArea.Right - Width));
                Top = Math.Max(SystemParameters.WorkArea.Top,
                               Math.Min(Top, SystemParameters.WorkArea.Bottom - Height));
            }

            if (persist)
            {
                Settings.Set("AppScale", scale.ToString("0.00", CultureInfo.InvariantCulture));
                ShowAppScaleReadout();
            }
        }

        private void ShowAppScaleReadout()
        {
            if (_appScaleHide == null)
            {
                _appScaleHide = new DispatcherTimer(DispatcherPriority.Normal)
                    { Interval = TimeSpan.FromSeconds(5) };
                _appScaleHide.Tick += (_, _) =>
                {
                    _appScaleHide.Stop();
                    StatusText.Text = _statusBeforeScale ?? Loc("Str_Status_Ready");
                    _statusBeforeScale = null;
                };
            }

            if (!_appScaleHide.IsEnabled) _statusBeforeScale = StatusText.Text;
            _appScaleHide.Stop();
            StatusText.Text = $"UI  {(int)Math.Round(_appScale * 100)}%";
            _appScaleHide.Start();
        }
    }
}
