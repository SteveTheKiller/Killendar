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
        private static readonly string[] InstalledFiles = { "Killendar.exe" };
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

        private static bool ShowInstallDialog(string message, bool confirmation = false, string? heading = null)
        {
            var dialog = new Controls.ConfirmDialog(heading ?? AppName, message,
                confirmation ? "Yes" : "OK", confirmation ? "No" : "");
            if (Current.MainWindow?.IsVisible == true)
                dialog.Owner = Current.MainWindow;
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.ShowInTaskbar = true;
            }
            dialog.ShowDialog();
            return dialog.Confirmed;
        }

        internal static void OfferInstallConflictRepair()
        {
            if (!File.Exists(InstallExe) || !File.Exists(MachineInstallExe)) return;
            string current = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            bool runningMachine = string.Equals(current, MachineInstallExe, StringComparison.OrdinalIgnoreCase);
            bool runningUser = string.Equals(current, InstallExe, StringComparison.OrdinalIgnoreCase);
            if (!runningMachine && !runningUser) return;

            string other = runningMachine ? "per-user" : "all-users";
            if (!ShowInstallDialog($"Killendar is installed twice. Remove the other {other} copy now?\n\nYour calendars and settings will not be removed.",
                true, AppName + " installation conflict")) return;

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
            try { File.Delete(Path.Combine(common, AppName + ".lnk")); } catch { }
            try { if (Directory.Exists(common)) Directory.Delete(common, false); } catch { }
            KillerFamily.Deployment.InstalledFileCleanup.Remove(MachineInstallDir, InstalledFiles);
            Services.CommandLinePath.Unregister(MachineInstallDir, machine: true);
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

                return new InstallRelaunch().StartInstalled(MachineInstallExe, () => Current.Shutdown());
            }

            if (!DoInstall(wantDesktop) || !File.Exists(InstallExe)) return false;
            return new InstallRelaunch().StartInstalled(InstallExe, () => Current.Shutdown());
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
            try { if (Directory.Exists(StartMenuDir)) Directory.Delete(StartMenuDir, false); } catch { }
            try { if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk); } catch { }
            KillerFamily.Deployment.InstalledFileCleanup.Remove(InstallDir, InstalledFiles);
            Services.CommandLinePath.Unregister(InstallDir, machine: false);
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
        // Silent install - winget / choco / RMM and the Microsoft Store
        // ============================================================

        private static int DoSilentInstall()
        {
            try
            {
                using var installLease = KillerFamily.Deployment.InstallerExitCodes.Acquire(AppName);
                string src = Process.GetCurrentProcess().MainModule!.FileName;
                var (valid, _, _) = CodeSignature.GetSignerInfo();
                if (!valid)
                {
                    Console.Error.WriteLine("Silent install refused: EXE has no valid Authenticode signature.");
                    return KillerFamily.Deployment.InstallerExitCodes.SecurityRejected;
                }

                // With admin rights (winget, choco, RMM) the install is machine-wide. Without them,
                // as the Microsoft Store runs installers, it is the normal per-user install with no
                // prompt. The app never needs admin rights.
                bool machine = KillerFamily.Deployment.InstallerExitCodes.IsElevated();
                string installDir = machine ? MachineInstallDir : InstallDir;
                string installExe = machine ? MachineInstallExe : InstallExe;
                string startMenuDir = machine
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName)
                    : StartMenuDir;
                string startMenuLnk = Path.Combine(startMenuDir, AppName + ".lnk");
                RegistryKey hive = machine ? Registry.LocalMachine : Registry.CurrentUser;

                Directory.CreateDirectory(installDir);
                if (File.Exists(installExe))
                {
                    try { File.SetAttributes(installExe, FileAttributes.Normal); } catch { }
                }
                KillerFamily.Deployment.InstalledImagePublication.Publish(src, installExe, () =>
                {
                    Directory.CreateDirectory(startMenuDir);
                    CreateShortcut(startMenuLnk, installExe);
                    WriteInstallKeys(hive, installDir, installExe);
                    FileAssociations.Register(hive, installExe);
                    Services.CommandLinePath.Register(installDir, machine);
                });
                return KillerFamily.Deployment.InstallerExitCodes.Success;
            }
            catch (Exception ex)
            {
                // No UI on this path by definition, so the exit code is the only signal a
                // deployment tool gets.
                Console.Error.WriteLine("Silent install failed: " + ex.Message);
                return KillerFamily.Deployment.InstallerExitCodes.FromException(ex);
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
                ShowInstallDialog(
                    "Installation refused: the running EXE does not carry a valid Authenticode " +
                    "signature.\n\nOnly signed builds of Killendar can be installed.");
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
                    var choice = ShowInstallDialog(
                        $"You are about to install an older version ({runningText}) over " +
                        $"the currently installed version ({installedText}).\n\nDowngrade anyway?",
                        true);
                    if (!choice) return false;
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
                    KillerFamily.Deployment.InstalledImagePublication.Publish(src, InstallExe, () =>
                    {
                        Directory.CreateDirectory(StartMenuDir);
                        CreateShortcut(StartMenuLnk, InstallExe);
                        if (wantDesktop) CreateShortcut(DesktopLnk, InstallExe);
                        WriteInstallKeys(Registry.CurrentUser, InstallDir, InstallExe);
                        FileAssociations.Register(Registry.CurrentUser, InstallExe);
                        Services.CommandLinePath.Register(InstallDir, machine: false);
                    });
                }
                catch (Exception copyEx) when (copyEx is UnauthorizedAccessException or IOException)
                {
                    ShowInstallDialog(
                        "Couldn't write the installed copy at:\n" + InstallExe +
                        "\n\n" + copyEx.Message +
                        "\n\nClose any open Killendar window, then run the installer again.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                ShowInstallDialog("Installation failed:\n" + ex.Message);
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
                key.SetValue("QuietUninstallString", "\"" + installExe + "\" /uninstall-silent");
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
        private static int? RelaunchMachineUninstallElevatedIfNeeded(bool machine, bool silent, string operationId)
        {
            return UninstallWorkflow.RequestElevation(machine,
                KillerFamily.Deployment.InstallerExitCodes.IsElevated,
                () => Process.Start(new ProcessStartInfo(
                    Process.GetCurrentProcess().MainModule!.FileName,
                    (silent ? "/uninstall-silent " : "/uninstall ") + operationId)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                }), ex =>
                {
                    string message = "Uninstall could not request administrator access:\n" + ex.Message;
                    if (silent) Console.Error.WriteLine(message);
                    else ShowInstallDialog(message);
                });
        }

        private static string UninstallStagingRoot(bool machine) => machine ? MachineInstallDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

        private static int RunUninstallHelper(string[] args)
        {
            if (args.Length != 3 || (args[1] != "machine" && args[1] != "user") || !Guid.TryParseExact(args[2], "N", out _))
                return KillerFamily.Deployment.InstallerExitCodes.SecurityRejected;
            bool machine = args[1] == "machine";
            if (machine && !KillerFamily.Deployment.InstallerExitCodes.IsElevated())
                return KillerFamily.Deployment.InstallerExitCodes.AccessDenied;
            return new DeferredUninstall().RunWorker(Process.GetCurrentProcess().MainModule!.FileName,
                UninstallStagingRoot(machine), machine ? MachineInstallDir : InstallDir, InstalledFiles,
                machine, args[2], () => RemoveUninstallMetadata(machine));
        }

        private static int Uninstall(bool silent, string? requestedOperationId)
        {
            string operationId = requestedOperationId ?? Guid.NewGuid().ToString("N");
            if (!Guid.TryParseExact(operationId, "N", out _)) return KillerFamily.Deployment.InstallerExitCodes.SecurityRejected;
            bool machine = string.Equals(Process.GetCurrentProcess().MainModule?.FileName,
                                         MachineInstallExe, StringComparison.OrdinalIgnoreCase);
            string operationDirectory = DeferredUninstall.OperationDirectory(UninstallStagingRoot(machine), operationId);
            int? elevatedResult = RelaunchMachineUninstallElevatedIfNeeded(machine, silent, operationId);
            if (elevatedResult.HasValue)
            {
                Console.Error.WriteLine("Uninstall result: " + elevatedResult.Value + ". Status and retained helper files: " + operationDirectory);
                return elevatedResult.Value;
            }

            if (!silent)
            {
                var confirm = new Controls.ConfirmDialog(
                    "Uninstall Killendar?",
                    "Your appointments will be kept.\n\nRemoval finishes after Killendar closes. Status and retained helper files:\n" + operationDirectory,
                    "Uninstall",
                    "Cancel")
                { WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = true };
                confirm.ShowDialog();
                if (!confirm.Confirmed) return KillerFamily.Deployment.InstallerExitCodes.Cancelled;
            }

            int result = new DeferredUninstall().Start(Process.GetCurrentProcess().MainModule!.FileName,
                UninstallStagingRoot(machine), machine ? MachineInstallDir : InstallDir, InstalledFiles, machine, operationId);
            Console.Error.WriteLine("Uninstall result: " + result + ". Status and retained helper files: " + operationDirectory);
            if (result != UninstallWorkflow.Pending && !silent)
                ShowInstallDialog("Uninstall could not start. Result: " + result + "\n\nRetained files and status, if created:\n" + operationDirectory);
            return result;
        }

        private static void RemoveUninstallMetadata(bool machine)
        {
            string startMenuDir = machine
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName)
                : StartMenuDir;
            string targetDir = machine ? MachineInstallDir : InstallDir;

            string shortcut = Path.Combine(startMenuDir, AppName + ".lnk");
            UninstallWorkflow.DeleteLiteral(shortcut);
            UninstallWorkflow.DeleteLiteral(DesktopLnk);
            if (!UninstallWorkflow.Absent(shortcut) || !UninstallWorkflow.Absent(DesktopLnk))
                throw new IOException("An installed shortcut remains.");
            if (Directory.Exists(startMenuDir) && Directory.GetFileSystemEntries(startMenuDir).Length == 0)
                Directory.Delete(startMenuDir, recursive: false);
            Services.CommandLinePath.Unregister(targetDir, machine);
            using (var environment = (machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(machine
                ? @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment" : "Environment"))
            {
                string value = environment?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                if (Services.CommandLinePath.WithoutEntry(value, targetDir) != value)
                    throw new IOException("The installed command path remains registered.");
            }
            if (!KillerFamily.Deployment.StartupRegistration.Set(Services.AppInfo.DisplayName, false))
                throw new UnauthorizedAccessException("The startup registration could not be removed.");
            using (var startup = Registry.CurrentUser.OpenSubKey(KillerFamily.Deployment.StartupRegistration.RunKeyPath))
                if (startup?.GetValue(Services.AppInfo.DisplayName) != null)
                    throw new IOException("The startup registration remains.");

            var hive = machine ? Registry.LocalMachine : Registry.CurrentUser;

            // Drop the association from the same scope that installed it. Also remove an HKCU
            // shadow left by older machine installs for the account performing this uninstall.
            if (machine)
            {
                FileAssociations.Unregister(Registry.LocalMachine);
                VerifyUninstallAssociations(Registry.LocalMachine);
                FileAssociations.Unregister(Registry.CurrentUser);
                VerifyUninstallAssociations(Registry.CurrentUser);
            }
            else
            {
                FileAssociations.Unregister(Registry.CurrentUser);
                VerifyUninstallAssociations(Registry.CurrentUser);
            }

            hive.DeleteSubKeyTree(RegKey, throwOnMissingSubKey: false);
            using (var remaining = hive.OpenSubKey(RegKey))
                if (remaining != null) throw new IOException("The install registration remains.");
            // Keep the uninstall entry available until every mandatory cleanup check succeeded.
            hive.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            using (var remaining = hive.OpenSubKey(UninstallKey))
                if (remaining != null) throw new IOException("The uninstall registration remains.");
        }

        private static void VerifyUninstallAssociations(RegistryKey hive)
        {
            foreach (string key in new[] { @"Software\Classes\" + FileAssociations.ProgId,
                @"Software\Classes\" + FileAssociations.IcsProgId, @"Software\Classes\Applications\Killendar.exe" })
            {
                using var remaining = hive.OpenSubKey(key);
                if (remaining != null) throw new IOException("A Killendar file association remains: " + key);
            }
            using (var calendar = hive.OpenSubKey(@"Software\Classes\" + EventStore.Extension))
                if (Equals(calendar?.GetValue(""), FileAssociations.ProgId))
                    throw new IOException("The Killendar calendar association remains.");
            using (var openWith = hive.OpenSubKey(@"Software\Classes\.ics\OpenWithProgids"))
                if (openWith?.GetValue(FileAssociations.IcsProgId) != null)
                    throw new IOException("The Killendar iCalendar association remains.");
        }
    }
}
