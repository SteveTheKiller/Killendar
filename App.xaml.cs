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
            CaptureOpenFileArgument(new[] { path! });
            if (PendingOpenFile != null) win.HandlePendingOpenFile();
        }

        // ============================================================
        // Startup
        // ============================================================

        protected override void OnStartup(StartupEventArgs e)
        {
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

            FileAssociations.Register();   // HKCU, best-effort, idempotent

            // Persistence hooks. ThemeManager uses these for theme and accent; the window chrome
            // uses the same pair for size, position and maximized state.
            Services.ThemeManager.GetSetting = Settings.Get;
            Services.ThemeManager.SetSetting = Settings.Set;

            // Restores the saved theme before the window is built, so there is no flash of the
            // default palette. With nothing saved this lands on Black + Red.
            Services.ThemeManager.Initialize();

            // Locale after the theme, and still before the window: the string dictionary has to be
            // in place before any {DynamicResource Str_*} in MainWindow.xaml is first resolved.
            Services.LocaleManager.GetSetting = Settings.Get;
            Services.LocaleManager.SetSetting = Settings.Set;
            Services.LocaleManager.Initialize();

            Services.DateFormatManager.GetSetting = Settings.Get;
            Services.DateFormatManager.SetSetting = Settings.Set;
            Services.DateFormatManager.Initialize();

            // Killendar.exe --demo
            // Rebuilds Demo.kcal with a full test calendar and switches to it, so the views can be
            // judged against real content instead of an empty grid. Deliberately AFTER the theme and
            // locale setup (the window is about to open on it) and after the single-instance claim
            // (rebuilding the file under a running instance would pull the rug out). Never touches
            // Default.kcal. Debug-only guard is intentionally absent - it is handy on a release
            // build too, and it cannot destroy real data.
            if (e.Args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)))
            {
                try { Services.DemoData.Build(); }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not build the demo Killendar: " + ex.Message,
                        AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            new MainWindow().Show();
        }

        // ============================================================
        // Install state
        // ============================================================

        /// <summary>True when this exe is running from somewhere other than an install location,
        /// i.e. it is the portable copy and could offer to install itself.</summary>
        internal static bool IsPortable()
        {
            try
            {
                string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
                return !string.Equals(currentExe, InstallExe, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(currentExe, MachineInstallExe, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>True when Killendar is already installed machine-wide.</summary>
        internal static bool MachineInstallExists() => File.Exists(MachineInstallExe);

        /// <summary>
        /// Install, then relaunch from the installed location. An all-users install re-runs this
        /// exe elevated with /silent - the same machine-wide path winget and choco use - so UAC
        /// only appears when the user actually asked for it. False if elevation was declined.
        /// </summary>
        internal static bool InstallAndRelaunch(bool wantDesktop, bool allUsers)
        {
            if (allUsers)
            {
                if (!RunElevatedSilentInstall()) return false;

                // Only ever one install: drop the per-user copy so there is a single Start Menu
                // entry and a single uninstall entry. Settings are deliberately left alone.
                RemovePerUserInstall();

                Process.Start(new ProcessStartInfo(MachineInstallExe));
                Current.Shutdown();
                return true;
            }

            DoInstall(wantDesktop);
            Process.Start(new ProcessStartInfo(InstallExe));
            Current.Shutdown();
            return true;
        }

        /// <summary>Re-run this exe elevated with /silent and wait for it to finish.</summary>
        private static bool RunElevatedSilentInstall()
        {
            try
            {
                var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule!.FileName, "/silent")
                {
                    UseShellExecute = true,
                    Verb = "runas",          // triggers the UAC prompt
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                return p is not null && p.ExitCode == 0 && File.Exists(MachineInstallExe);
            }
            catch
            {
                // Declining the UAC prompt throws Win32Exception 1223 (ERROR_CANCELLED).
                return false;
            }
        }

        /// <summary>Remove a per-user install: files, shortcuts and its HKCU install markers.
        /// The settings file in LOCALAPPDATA is deliberately left alone so theme, accent, locale
        /// and window placement survive the move to a machine-wide install.</summary>
        private static void RemovePerUserInstall()
        {
            try { if (File.Exists(StartMenuLnk)) File.Delete(StartMenuLnk); } catch { }
            try { if (Directory.Exists(StartMenuDir)) Directory.Delete(StartMenuDir, true); } catch { }
            try { if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk); } catch { }
            try { if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true); } catch { }
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
                key?.DeleteValue("Installed", throwOnMissingValue: false);
                key?.DeleteValue("InstallPath", throwOnMissingValue: false);
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); }
            catch { }
        }

        // ============================================================
        // Silent (machine-wide) install - winget / choco / RMM
        // ============================================================

        private static void DoSilentInstall()
        {
            try
            {
                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
                string startMenuLnk = Path.Combine(startMenuDir, AppName + ".lnk");

                Directory.CreateDirectory(MachineInstallDir);
                string src = Process.GetCurrentProcess().MainModule!.FileName;
                File.Copy(src, MachineInstallExe, overwrite: true);

                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(startMenuLnk, MachineInstallExe);

                WriteInstallKeys(Registry.LocalMachine, MachineInstallDir, MachineInstallExe);
            }
            catch (Exception ex)
            {
                // No UI on this path by definition, so the exit code is the only signal a
                // deployment tool gets.
                Console.Error.WriteLine("Silent install failed: " + ex.Message);
                Environment.Exit(1);
            }
        }

        // ============================================================
        // Per-user install
        // ============================================================

        private static void DoInstall(bool wantDesktop)
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                string src = Process.GetCurrentProcess().MainModule!.FileName;
                File.Copy(src, InstallExe, overwrite: true);

                Directory.CreateDirectory(StartMenuDir);
                CreateShortcut(StartMenuLnk, InstallExe);
                if (wantDesktop) CreateShortcut(DesktopLnk, InstallExe);

                WriteInstallKeys(Registry.CurrentUser, InstallDir, InstallExe);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Installation failed:\n" + ex.Message, AppName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Install markers plus the Add/Remove Programs entry. Same shape for both
        /// scopes; only the hive and the paths differ.</summary>
        private static void WriteInstallKeys(RegistryKey hive, string installDir, string installExe)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

            using (var key = hive.CreateSubKey(RegKey))
            {
                key.SetValue("Installed", 1);
                key.SetValue("InstallPath", installExe);
                key.SetValue("Version", version);
            }

            using (var key = hive.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "Steve / thekiller.net");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("DisplayIcon", installExe + ",0");
                key.SetValue("UninstallString", "\"" + installExe + "\" /uninstall");
                key.SetValue("QuietUninstallString", "\"" + installExe + "\" /uninstall");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
            }
        }

        private static void CreateShortcut(string lnkPath, string targetPath)
        {
            // Reflection over IDispatch rather than `dynamic`, so this does not need the
            // Microsoft.CSharp runtime binder at run time.
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null) return;
                object shell = Activator.CreateInstance(shellType)!;
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath })!;
                var sc = shortcut.GetType();
                sc.InvokeMember("TargetPath", BindingFlags.SetProperty,
                    null, shortcut, new object[] { targetPath });
                sc.InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                    null, shortcut, new object[] { Path.GetDirectoryName(targetPath)! });
                sc.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { /* best-effort: a missing shortcut is not worth failing an install over */ }
        }

        // ============================================================
        // Uninstall
        // ============================================================

        private static void Uninstall()
        {
            // A stock MessageBox on purpose: this path runs with no main window, so none of the
            // themed chrome (or its resource dictionaries) is loaded to draw a ConfirmDialog.
            var res = MessageBox.Show(
                "Uninstall Killendar from this computer?",
                AppName + " Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            bool machine = File.Exists(MachineInstallExe) &&
                           string.Equals(Process.GetCurrentProcess().MainModule?.FileName,
                                         MachineInstallExe, StringComparison.OrdinalIgnoreCase);

            string startMenuDir = machine
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName)
                : StartMenuDir;
            string targetDir = machine ? MachineInstallDir : InstallDir;

            try { File.Delete(Path.Combine(startMenuDir, AppName + ".lnk")); } catch { }
            try { Directory.Delete(startMenuDir, recursive: false); } catch { }
            try { File.Delete(DesktopLnk); } catch { }

            var hive = machine ? Registry.LocalMachine : Registry.CurrentUser;
            try { hive.DeleteSubKeyTree(RegKey, throwOnMissingSubKey: false); } catch { }
            try { hive.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }

            // Drop the .kcal association before the exe goes: a ProgID pointing at a deleted file
            // is how you get a broken Open With list that nothing in the UI offers to clean up.
            FileAssociations.Unregister();

            // Self-delete, deferred through a batch file so this exe can exit first. Appointments
            // in APPDATA are deliberately NOT touched: uninstalling the app should not throw away
            // the user's calendar.
            string bat = Path.Combine(Path.GetTempPath(), "killendar_uninstall.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping -n 3 127.0.0.1 >nul\r\n" +
                "rmdir /s /q \"" + targetDir + "\"\r\n" +
                "del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            });

            MessageBox.Show("Killendar has been uninstalled. Your appointments were left in place.",
                AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Flat key/value settings in %LOCALAPPDATA%\Killendar\settings.json. Deliberately dumb: the
    /// kit only ever stores short strings (theme name, accent name, locale, window rect), and a
    /// JSON file keeps the app xcopy-portable, with no registry footprint to clean up.
    /// Every read and write is best-effort - a corrupt or unwritable file costs the saved theme,
    /// never a crash on startup.
    /// </summary>
    internal static class Settings
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Killendar");
        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        private static Dictionary<string, string>? _cache;

        private static Dictionary<string, string> Load()
        {
            if (_cache != null) return _cache;
            try
            {
                _cache = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))
                      ?? new Dictionary<string, string>()
                    : new Dictionary<string, string>();
            }
            catch { _cache = new Dictionary<string, string>(); }
            return _cache;
        }

        internal static string? Get(string key)
        {
            var d = Load();
            return d.TryGetValue(key, out var v) ? v : null;
        }

        internal static void Set(string key, string value)
        {
            var d = Load();
            d[key] = value;
            try
            {
                Directory.CreateDirectory(Dir);
                // Write to a temp file and swap, so a crash mid-write cannot leave a truncated
                // settings file that loses every stored key at once.
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { /* unwritable profile - the setting just does not persist */ }
        }
    }
}
