using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using Killendar.Controls;

// Window-message plumbing for a WindowStyle="None" window, and the caption buttons. This stays a
// partial of MainWindow on purpose: every method here needs the window's own HWND, its WindowState
// or its named caption buttons, so there is nothing to inject and nothing to test in isolation.
//
// The pieces that were NOT window-specific now live as real classes:
//   Controls/DwmChrome        - rounded corners and the themed frame border
//   Controls/WindowPlacement  - size/position persistence
//   Controls/GrainTexture     - the shared film-grain tile
//
// MainWindow.xaml is expected to name: RootGrid (Opacity="0" so FadeInContent can reveal it),
// MinimizeBtn / MaximizeBtn / CloseBtn, ResizeGrip, and any *GrainBrush layers.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        // Grain brush layers in MainWindow.xaml. All optional - Apply skips the ones absent.
        private static readonly string[] GrainBrushNames =
            { "GrainBrush", "TitleGrainBrush", "ToolbarGrainBrush", "StatusGrainBrush", "FlyoutGrainBrush" };

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            DwmChrome.SetRoundedCorners(this, rounded: WindowState == WindowState.Normal);
            DwmChrome.SetThemeBorder(this);
        }

        private void ApplyGrainTexture() => GrainTexture.Apply(this, GrainBrushNames);

        /// <summary>Content fade-in on open; RootGrid starts at Opacity="0" in XAML.</summary>
        private void FadeInContent() => Anim.FadeIn(RootGrid);

        private void RestoreWindowPlacement()
            => WindowPlacement.Restore(this, Services.ThemeManager.GetSetting);

        protected override void OnClosed(EventArgs e)
        {
            WindowPlacement.Save(this, Services.ThemeManager.SetSetting);
            base.OnClosed(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // Square the corners when maximized (flush to the screen edges), round when floating.
            DwmChrome.SetRoundedCorners(this, rounded: WindowState == WindowState.Normal);
            // Maximize glyph (Segoe MDL2) toggles to a restore glyph when maximized.
            if (MaximizeBtn != null)
                MaximizeBtn.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        // ---- window messages ----

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_ERASEBKGND    = 0x0014;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const int HTBOTTOMRIGHT = 17;
        private const int HTCAPTION = 2;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_ERASEBKGND)
            {
                // WPF paints the whole client area itself, so nothing should erase the background to
                // a flat fill during a resize - that erase IS the white flash. Claim the message.
                handled = true;
                return new IntPtr(1);
            }
            if (msg == WM_GETMINMAXINFO)
            {
                // A WindowStyle="None" window maximizes over the taskbar unless we clamp it to the
                // monitor's work area ourselves.
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(monitor, ref info);
            RECT work = info.rcWork;
            RECT mon = info.rcMonitor;

            mmi.ptMaxPosition.x = Math.Abs(work.left - mon.left);
            mmi.ptMaxPosition.y = Math.Abs(work.top - mon.top);
            mmi.ptMaxSize.x = Math.Abs(work.right - work.left);
            mmi.ptMaxSize.y = Math.Abs(work.bottom - work.top);
            mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
            mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Our own grip: WPF's CanResizeWithGrip dots land out in the transparent shadow
        /// margin, so we draw one at the content corner and forward the resize to Windows.</summary>
        private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (WindowState != WindowState.Normal) return;
            e.Handled = true;
            SendMessage(new WindowInteropHelper(this).Handle,
                        WM_NCLBUTTONDOWN, new IntPtr(HTBOTTOMRIGHT), IntPtr.Zero);
        }

        /// <summary>Lets the toolbar drag the window like the title bar. Interactive controls handle
        /// their own clicks, so only the bar's empty space bubbles up here. Native HTCAPTION also
        /// gives correct restore-from-maximized-and-drag behaviour for free.</summary>
        private void Toolbar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            SendMessage(new WindowInteropHelper(this).Handle,
                        WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // ---- caption buttons (drag and double-click-maximize are native via WindowChrome) ----

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
