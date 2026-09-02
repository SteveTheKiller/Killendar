using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Killendar.Services;
using Killendar.Shell;
using Microsoft.Win32;

// Application entry point and install system. The exe installs itself, per-user by default or
// machine-wide via /silent, and uninstalls itself from Add/Remove Programs. There is no separate
// installer package to keep in sync with the app.
namespace Killendar
{
    public partial class App : Application
    {
        private const string RegKey = @"Software\Killendar";
        private const string UninstallKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Killendar";

        private static readonly string AppName = "Killendar";
        private static readonly string ExeName = "Killendar.exe";

        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", AppName);
        private static readonly string InstallExe = Path.Combine(InstallDir, ExeName);

        // Machine-wide ("all users") target. Used by the /silent path that winget, choco and RMMs
        // call, and by the Install for all users option on the install prompt.
        private static readonly string MachineInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
        private static readonly string MachineInstallExe = Path.Combine(MachineInstallDir, ExeName);

        private static readonly string StartMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
        private static readonly string StartMenuLnk = Path.Combine(StartMenuDir, AppName + ".lnk");
        private static readonly string DesktopLnk = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        // ============================================================
        // Shell integration
        // ============================================================

        private SingleInstanceGuard? _instance;

        /// <summary>Path of a double-clicked .kcal, waiting for the window to finish opening.
        /// Internal so MainWindow can drain it, and so a forwarded second launch can refill it.</summary>
        internal static string? PendingOpenFile;

        private static void CaptureOpenFileArgument(string[] args)
        {
            var path = FileAssociations.CalendarPathFrom(args);
            if (path != null) PendingOpenFile = path;
        }

        /// <summary>A second launch was blocked and forwarded its command line here: bring the window
        /// forward, then route any .kcal through the same path as a first-launch double-click.</summary>
        private void OnForwardedLaunch(string? path)
        {
            if (MainWindow is not MainWindow win) return;

            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Activate();
            win.Topmost = true; win.Topmost = false;   // foreground nudge past the focus rules

            if (string.IsNullOrEmpty(path)) return;
            CaptureOpenFileArgument([path!]);
            if (PendingOpenFile != null) win.HandlePendingOpenFile();
        }

        // ============================================================
        // Startup
        // ============================================================

        protected override void OnStartup(StartupEventArgs e)
        {
            HookCrashLogging();   // CrashLog.cs - first, so it covers startup itself
            base.OnStartup(e);

            // Render on the CPU so the window is not black over console-session
            // screen-sharing tools (ScreenConnect, Kaseya LiveConnect, VNC, TeamViewer).
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;

            // Silent install: Killendar.exe /silent
            // Installs machine-wide to Program Files, no UI. Used by winget/choco/RMM.
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "/silent", StringComparison.OrdinalIgnoreCase))
            {
                DoSilentInstall();
                Shutdown(0);
                return;
            }

            // Uninstall flag, called by Add/Remove Programs.
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "/uninstall", StringComparison.OrdinalIgnoreCase))
            {
                Uninstall();
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && string.Equals(e.Args[0], "/remove-machine-conflict", StringComparison.OrdinalIgnoreCase))
            {
                RemoveMachineInstallConflict();
                Shutdown(0);
                return;
            }

            // A double-clicked .kcal arrives as argv[0]. Captured before the single-instance check
            // so a blocked second launch can forward it to the running window.
            CaptureOpenFileArgument(e.Args);

            // One Killendar per desktop session. Must come AFTER the /silent and /uninstall paths
            // above: those are meant to run alongside a live instance.
            _instance = new SingleInstanceGuard(AppName, Dispatcher, OnForwardedLaunch);
            if (!_instance.Claim(PendingOpenFile))
            {
                Shutdown(0);
                return;
            }

            string runningExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.Equals(runningExe, MachineInstallExe, StringComparison.OrdinalIgnoreCase))
                FileAssociations.Register();   // portable/per-user: HKCU, best-effort, idempotent
            OfferInstallConflictRepair();

            // Persistence hooks. ThemeManager uses these for theme and accent; the window chrome
            // uses the same pair for size, position and maximized state.
            Services.ThemeManager.GetSetting = Settings.Get;
            Services.ThemeManager.SetSetting = Settings.Set;

            // Restores the saved theme before the window is built, so there is no flash of the
            // default palette. With nothing saved this lands on Black + Red.
            Services.ThemeManager.Initialize();

            // Capture the regional week start before the selected interface locale changes the
            // process culture used for generated month and weekday names.
            DayOfWeek windowsFirstDay = System.Globalization.CultureInfo.CurrentCulture
                .DateTimeFormat.FirstDayOfWeek;

            // Locale after the theme, and still before the window: the string dictionary has to be
            // in place before any {DynamicResource Str_*} in MainWindow.xaml is first resolved.
            Services.LocaleManager.GetSetting = Settings.Get;
            Services.LocaleManager.SetSetting = Settings.Set;
            Services.LocaleManager.Initialize();

            Services.DateFormatManager.GetSetting = Settings.Get;
            Services.DateFormatManager.SetSetting = Settings.Set;
            Services.DateFormatManager.Initialize();

            Services.WeekStartManager.GetSetting = Settings.Get;
            Services.WeekStartManager.SetSetting = Settings.Set;
            Services.WeekStartManager.Initialize(windowsFirstDay);

            // Killendar.exe --demo
            // Rebuilds Demo.kcal with a full test calendar and switches to it, so the views can be
            // judged against real content instead of an empty grid. Deliberately AFTER the theme and
            // locale setup (the window is about to open on it) and after the single-instance claim
            // (rebuilding the file under a running instance would pull the rug out). Never touches
            // Default.kcal. Debug-only guard is intentionally absent - it is handy on a release
            // build too, and it cannot destroy real data.
            if (e.Args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)))
            {
                IsDemo = true;
                try { Services.DemoData.Build(); }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not build the demo Killendar: " + ex.Message,
                        AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            new MainWindow().Show();
        }

        /// <summary>True when launched with --demo. Read by the shell to keep marketing
        /// screenshots clean - the portable badge is suppressed in demo mode.</summary>
        internal static bool IsDemo { get; private set; }
    }
}
