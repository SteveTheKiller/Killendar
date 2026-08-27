using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Killendar.Services;
using Microsoft.Win32;

namespace Killendar
{
    public partial class App
    {
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

        internal static void OfferInstallConflictRepair()
        {
            if (!File.Exists(InstallExe) || !File.Exists(MachineInstallExe)) return;
            string current = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            bool runningMachine = string.Equals(current, MachineInstallExe, StringComparison.OrdinalIgnoreCase);
            bool runningUser = string.Equals(current, InstallExe, StringComparison.OrdinalIgnoreCase);
            if (!runningMachine && !runningUser) return;

            string other = runningMachine ? "per-user" : "all-users";
            if (MessageBox.Show($"Killendar is installed twice. Remove the other {other} copy now?\n\nYour calendars and settings will not be removed.",
                AppName + " installation conflict", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            if (runningMachine) RemovePerUserInstall();
            else
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(current, "/remove-machine-conflict")
                    { UseShellExecute = true, Verb = "runas" });
                    p?.WaitForExit();
                }
                catch { }
            }
        }

        private static void RemoveMachineInstallConflict()
        {
            string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
            try { Registry.LocalMachine.DeleteSubKeyTree(RegKey, false); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }
            FileAssociations.Unregister(Registry.LocalMachine);
            try { if (Directory.Exists(common)) Directory.Delete(common, true); } catch { }
            try { if (Directory.Exists(MachineInstallDir)) Directory.Delete(MachineInstallDir, true); } catch { }
        }

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

            if (!DoInstall(wantDesktop) || !File.Exists(InstallExe)) return false;
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
            FileAssociations.Unregister(Registry.CurrentUser);
        }

        // ============================================================
        // Silent (machine-wide) install - winget / choco / RMM
        // ============================================================

        private static void DoSilentInstall()
        {
            try
            {
                string src = Process.GetCurrentProcess().MainModule!.FileName;
                var (valid, _, _) = CodeSignature.GetSignerInfo();
                if (!valid)
                {
                    Console.Error.WriteLine("Silent install refused: EXE has no valid Authenticode signature.");
                    Environment.Exit(1);
                    return;
                }

                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
                string startMenuLnk = Path.Combine(startMenuDir, AppName + ".lnk");

                Directory.CreateDirectory(MachineInstallDir);
                if (File.Exists(MachineInstallExe))
                {
                    try { File.SetAttributes(MachineInstallExe, FileAttributes.Normal); } catch { }
                }
                File.Copy(src, MachineInstallExe, overwrite: true);
                try { File.SetAttributes(MachineInstallExe, FileAttributes.Normal); } catch { }

                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(startMenuLnk, MachineInstallExe);

                WriteInstallKeys(Registry.LocalMachine, MachineInstallDir, MachineInstallExe);
                FileAssociations.Register(Registry.LocalMachine, MachineInstallExe);
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

        private static bool DoInstall(bool wantDesktop)
        {
            string src = Process.GetCurrentProcess().MainModule!.FileName;

            var (valid, _, _) = CodeSignature.GetSignerInfo();
            if (!valid)
            {
                MessageBox.Show(
                    "Installation refused: the running EXE does not carry a valid Authenticode " +
                    "signature.\n\nOnly signed builds of Killendar can be installed.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (File.Exists(InstallExe))
            {
                string runningText = FileVersionInfo.GetVersionInfo(src).FileVersion ?? "";
                string installedText = FileVersionInfo.GetVersionInfo(InstallExe).FileVersion ?? "";
                if (Version.TryParse(runningText, out var runningVersion) &&
                    Version.TryParse(installedText, out var installedVersion) &&
                    runningVersion < installedVersion)
                {
                    var choice = MessageBox.Show(
                        $"You are about to install an older version ({runningText}) over " +
                        $"the currently installed version ({installedText}).\n\nDowngrade anyway?",
                        AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.Yes) return false;
                }
            }

            try
            {
                Directory.CreateDirectory(InstallDir);
                if (File.Exists(InstallExe))
                {
                    try { File.SetAttributes(InstallExe, FileAttributes.Normal); } catch { }
                }
                try
                {
                    File.Copy(src, InstallExe, overwrite: true);
                }
                catch (Exception copyEx) when (copyEx is UnauthorizedAccessException or IOException)
                {
                    MessageBox.Show(
                        "Couldn't write the installed copy at:\n" + InstallExe +
                        "\n\nClose any open Killendar window (and check Task Manager for " +
                        "Killendar.exe), then run the installer again.",
                        AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                try { File.SetAttributes(InstallExe, FileAttributes.Normal); } catch { }

                Directory.CreateDirectory(StartMenuDir);
                CreateShortcut(StartMenuLnk, InstallExe);
                if (wantDesktop) CreateShortcut(DesktopLnk, InstallExe);

                WriteInstallKeys(Registry.CurrentUser, InstallDir, InstallExe);
                FileAssociations.Register(Registry.CurrentUser, InstallExe);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Installation failed:\n" + ex.Message, AppName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
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
                key.SetValue("Publisher", "Steve the Killer");
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
                    BindingFlags.InvokeMethod, null, shell, [lnkPath])!;
                var sc = shortcut.GetType();
                sc.InvokeMember("TargetPath", BindingFlags.SetProperty,
                    null, shortcut, [targetPath]);
                sc.InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                    null, shortcut, [Path.GetDirectoryName(targetPath)!]);
                sc.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { /* best-effort: a missing shortcut is not worth failing an install over */ }
        }

        // ============================================================
        // Uninstall
        // ============================================================

        /// <summary>Machine-wide uninstall entries launch an asInvoker executable. If the
        /// Program Files copy is not already elevated, relaunch that same copy with UAC before
        /// showing the confirmation or touching HKLM/Program Files.</summary>
        private static bool RelaunchMachineUninstallElevatedIfNeeded(bool machine)
        {
            if (!machine) return false;
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                    return false;

                Process.Start(new ProcessStartInfo(
                    Process.GetCurrentProcess().MainModule!.FileName, "/uninstall")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // UAC was declined. Leave the installation untouched.
            }
            catch (Exception ex)
            {
                MessageBox.Show("Uninstall could not request administrator access:\n" + ex.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

        private static void Uninstall()
        {
            bool machine = string.Equals(Process.GetCurrentProcess().MainModule?.FileName,
                                         MachineInstallExe, StringComparison.OrdinalIgnoreCase);
            if (RelaunchMachineUninstallElevatedIfNeeded(machine)) return;

            var confirm = new Controls.ConfirmDialog(
                "Uninstall Killendar?",
                "Your appointments will be kept.",
                "Uninstall",
                "Cancel");
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

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

            // Drop the association from the same scope that installed it. Also remove an HKCU
            // shadow left by older machine installs for the account performing this uninstall.
            if (machine)
            {
                FileAssociations.Unregister(Registry.LocalMachine);
                FileAssociations.Unregister(Registry.CurrentUser);
            }
            else
            {
                FileAssociations.Unregister(Registry.CurrentUser);
            }

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

        }
    }
}
