using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Killendar.Shell
{
    /// <summary>The appointment sidebar's user-sized inner edge, matching KillerPDF's grip.</summary>
    public partial class MainWindow
    {
        private const double SidebarMinW = 240;
        private const double SidebarMaxW = 520;
        private const string SidebarWidthKey = "AppointmentSidebarWidth";

        private void InitSidebarResize()
        {
            if (double.TryParse(Settings.Get(SidebarWidthKey), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var saved))
                _sidebarWidth = ClampSidebarWidth(saved);

            SidebarContent.Width = _sidebarWidth;
            SidebarSplitter.IsEnabled = false;
        }

        private void SidebarGrip_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (!_sidebarOpen) return;
            // A held GridLength animation owns Width and would ignore direct drag assignments.
            SidebarCol.BeginAnimation(ColumnDefinition.WidthProperty, null);
        }

        private void SidebarGrip_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_sidebarOpen || _riding) return;

            double width = ClampSidebarWidth(SidebarCol.ActualWidth + e.HorizontalChange);
            _sidebarWidth = width;
            SidebarContent.Width = width;
            SidebarCol.Width = new GridLength(width);
        }

        private void SidebarGrip_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (!_sidebarOpen) return;
            _sidebarWidth = ClampSidebarWidth(SidebarCol.ActualWidth);
            SidebarContent.Width = _sidebarWidth;
            SidebarCol.Width = new GridLength(_sidebarWidth);
            Settings.Set(SidebarWidthKey, _sidebarWidth.ToString(CultureInfo.InvariantCulture));
            MinWidth = Math.Min(OpenMinWidth, SystemParameters.WorkArea.Width);
        }

        private static double ClampSidebarWidth(double width) =>
            Math.Max(SidebarMinW, Math.Min(SidebarMaxW, width));
    }
}
