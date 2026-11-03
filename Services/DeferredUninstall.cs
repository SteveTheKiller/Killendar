using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.InteropServices;
using KillerFamily.Deployment;

namespace Killendar.Services
{
    internal sealed class DeferredUninstall
    {
        public const string HelperArgument = "--complete-uninstall";
        public const string WorkerName = "Killendar.Uninstall.exe";
        public const string ReceiptName = "status.json";
        private readonly IInstallRelaunchProcesses _processes;

        public DeferredUninstall() : this(new InstallRelaunch.SystemProcesses()) { }
        public DeferredUninstall(IInstallRelaunchProcesses processes) => _processes = processes;

        public static bool IsHelperRequest(string[] args, string currentImage, string machineStagingRoot, string userStagingRoot)
        {
            if (Array.Exists(args, argument => string.Equals(argument, HelperArgument, StringComparison.OrdinalIgnoreCase))) return true;
            if (!string.Equals(Path.GetFileName(currentImage), WorkerName, StringComparison.OrdinalIgnoreCase)) return false;
            string? operation = Path.GetDirectoryName(Path.GetFullPath(currentImage));
            if (operation == null) return false;
            string name = Path.GetFileName(operation);
            const string prefix = "Uninstall-";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(name.Substring(prefix.Length), "N", out _)) return false;
            string? root = Path.GetDirectoryName(operation);
            return string.Equals(root, Path.GetFullPath(machineStagingRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || string.Equals(root, Path.GetFullPath(userStagingRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        public static string OperationDirectory(string root, string operationId)
        {
            if (!Guid.TryParseExact(operationId, "N", out _)) throw new ArgumentException("Invalid uninstall operation identity.");
            return Path.Combine(Path.GetFullPath(root), "Uninstall-" + operationId);
        }

        public int Start(string currentImage, string stagingRoot, string targetDirectory,
            string[] ownedFiles, bool machine, string operationId)
        {
            string directory = OperationDirectory(stagingRoot, operationId);
            string receiptPath = Path.Combine(directory, ReceiptName);
            var receipt = new UninstallWorkflow.Receipt
            {
                OperationId = operationId, Machine = machine,
                TargetDirectory = Path.GetFullPath(targetDirectory),
                RemainingFiles = ownedFiles.Select(file => Path.Combine(Path.GetFullPath(targetDirectory), file)).ToArray()
            };
            bool created = false;
            try
            {
                ValidateOwned(targetDirectory, ownedFiles);
                CreateOperationDirectory(directory, machine);
                created = true;
                using var parent = _processes.Current();
                receipt.ParentId = parent.Id;
                receipt.ParentCreationFileTime = parent.CreationFileTime;
                string worker = Path.Combine(directory, WorkerName);
                using (var source = new FileStream(currentImage, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destination = new FileStream(worker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { source.CopyTo(destination); destination.Flush(true); }
                receipt.ImageHash = Hash(currentImage);
                if (!string.Equals(receipt.ImageHash, Hash(worker), StringComparison.Ordinal))
                    throw new System.Security.SecurityException("The uninstall helper copy did not match the running image.");
                if (File.Exists(currentImage + ".config")) File.Copy(currentImage + ".config", worker + ".config", false);
                return UninstallWorkflow.StartDeferred(receiptPath, () => _processes.Start(new ProcessStartInfo(worker,
                    HelperArgument + " " + (machine ? "machine" : "user") + " " + operationId)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = directory
                }), receipt);
            }
            catch (Exception error)
            {
                receipt.State = "Failed";
                receipt.ExitCode = InstallerExitCodes.FromException(error);
                receipt.Error = error.Message;
                // Never publish into a directory that this operation did not create.
                if (created)
                    try { UninstallWorkflow.WriteReceipt(receiptPath, receipt); } catch { }
                return receipt.ExitCode;
            }
        }

        /// <summary>Marks a file or an empty folder for deletion at the next restart. Replaced in tests.</summary>
        internal static Func<string, bool> ScheduleRemoval = path => MoveFileEx(path, null, MoveFileDelayUntilReboot);

        public int RunWorker(string currentImage, string stagingRoot, string targetDirectory,
            string[] ownedFiles, bool machine, string operationId, Action removeMetadata,
            int parentWaitMilliseconds = 30000, int attempts = 30, int retryMilliseconds = 1000,
            Action? removeIntegration = null, Func<bool>? isRunning = null, int runningWaitMilliseconds = 30000)
        {
            string directory = OperationDirectory(stagingRoot, operationId);
            string path = Path.Combine(directory, ReceiptName);
            UninstallWorkflow.Receipt? receipt = null;
            try
            {
                ValidateOwned(targetDirectory, ownedFiles);
                RejectLinks(directory);
                if (!string.Equals(Path.GetFullPath(currentImage), Path.Combine(directory, WorkerName), StringComparison.OrdinalIgnoreCase))
                    throw new System.Security.SecurityException("The uninstall worker is outside its operation directory.");
                receipt = UninstallWorkflow.ReadReceipt(path);
                if (receipt.OperationId != operationId || receipt.Machine != machine || receipt.State != "Pending"
                    || receipt.ExitCode != UninstallWorkflow.Pending || receipt.ParentId <= 0 || receipt.ParentCreationFileTime <= 0
                    || !string.Equals(receipt.TargetDirectory, Path.GetFullPath(targetDirectory), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(receipt.ImageHash, Hash(currentImage), StringComparison.Ordinal))
                    throw new System.Security.SecurityException("The uninstall receipt does not match this worker.");
                using var current = _processes.Current();
                if (current.Id == receipt.ParentId) throw new System.Security.SecurityException("The uninstall worker cannot wait on itself.");
                using var parent = _processes.Find(receipt.ParentId);
                if (parent != null && parent.CreationFileTime != receipt.ParentCreationFileTime)
                    throw new System.Security.SecurityException("The uninstall parent identity changed.");
                if (parent != null && !parent.WaitForExit(parentWaitMilliseconds))
                    throw new System.ComponentModel.Win32Exception(InstallerExitCodes.Busy, "The uninstall parent has not exited.");
                int result = UninstallWorkflow.CompleteDeferred(targetDirectory, ownedFiles, () =>
                {
                    removeMetadata();
                    receipt.MetadataRemoved = true;
                }, attempts, retryMilliseconds, error => receipt.Error = error.Message, removeIntegration,
                    isRunning, runningWaitMilliseconds);
                receipt.RemainingFiles = ownedFiles.Select(file => Path.Combine(targetDirectory, file))
                    .Where(file => !UninstallWorkflow.Absent(file)).ToArray();
                receipt.State = result == 0 ? "Completed" : "Failed";
                receipt.ExitCode = result;
                if (result != 0 && receipt.Error.Length == 0) receipt.Error = "Uninstall cleanup did not complete. Retained files are listed in this receipt.";
                UninstallWorkflow.WriteReceipt(path, receipt);
                if (result == 0 && machine) ScheduleMachineCleanup(directory, targetDirectory, ScheduleRemoval);
                return result;
            }
            catch (Exception error)
            {
                int result = InstallerExitCodes.FromException(error);
                if (receipt != null)
                {
                    receipt.State = "Failed"; receipt.ExitCode = result; receipt.Error = error.Message;
                    try { UninstallWorkflow.WriteReceipt(path, receipt); } catch { }
                }
                return result;
            }
        }

        /// <summary>The installed files plus the update working files (.old-, .update-, .recovery.txt,
        /// .bak) an update left beside the exe, by name. Computed at uninstall time so leftovers never
        /// keep the folder. Every name still passes InstalledFileCleanup.OwnedPaths.</summary>
        internal static string[] OwnedFiles(string directory, string[] installedFiles, string exe = "Killendar.exe")
            => installedFiles.Concat(ExecutableDeployment.UninstallLeftovers(directory, exe))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>Replaced in tests. Starts the detached cleanup process.</summary>
        internal static Func<ProcessStartInfo, bool> StartCleanup = start =>
        {
            using var process = Process.Start(start);
            return process != null;
        };

        /// <summary>A per-user uninstall runs from a worker copy in the operation folder, which cannot
        /// delete itself. A hidden PowerShell outlives the worker, retries until the folder is
        /// free, removes it, then removes the install folder if nothing else is in it.</summary>
        internal static void SchedulePerUserCleanup(string operationDirectory, string targetDirectory, Func<ProcessStartInfo, bool> start)
        {
            try
            {
                static string Quote(string path) => "'" + Path.GetFullPath(path).Replace("'", "''") + "'";
                string script = "for($i=0;$i -lt 60;$i++){try{[IO.Directory]::Delete(" + Quote(operationDirectory)
                    + ",$true);break}catch{Start-Sleep 1}};try{[IO.Directory]::Delete(" + Quote(targetDirectory) + ")}catch{}";
                string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                string shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                start(new ProcessStartInfo(shell, "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { /* Best effort: the uninstall itself already succeeded. */ }
        }

        /// <summary>A machine-wide uninstall runs from a copy staged inside the install folder, which
        /// cannot delete itself. Once it succeeds, the copy, its folder and the install folder (empty
        /// by then) are left for Windows to remove at the next restart.</summary>
        internal static void ScheduleMachineCleanup(string operationDirectory, string targetDirectory, Func<string, bool> schedule)
        {
            RemoveFailedAttempts(operationDirectory);
            try
            {
                foreach (string name in new[] { WorkerName, WorkerName + ".config", ReceiptName })
                {
                    string file = Path.Combine(operationDirectory, name);
                    if (File.Exists(file)) schedule(file);
                }
                schedule(operationDirectory);
                schedule(Path.GetFullPath(targetDirectory));
            }
            catch { /* Best effort: the uninstall itself already succeeded. */ }
        }

        /// <summary>A failed uninstall keeps its worker and receipt so the reason can be read, and on a
        /// machine install those sit in the install folder, which then could not go at the restart.
        /// Once an uninstall succeeds they have no further use. Only a folder holding nothing but a
        /// failed attempt is removed.</summary>
        private static void RemoveFailedAttempts(string operationDirectory)
        {
            const string prefix = "Uninstall-";
            string[] folders;
            try { folders = Directory.GetDirectories(Path.GetDirectoryName(operationDirectory)!, prefix + "*"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
            var attempt = new[] { WorkerName, WorkerName + ".config", ReceiptName };
            foreach (string folder in folders)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(folder), Path.GetFullPath(operationDirectory), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Guid.TryParseExact(Path.GetFileName(folder).Substring(prefix.Length), "N", out _)) continue;
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                    string[] entries = Directory.GetFileSystemEntries(folder);
                    if (entries.Any(entry => !attempt.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase))) continue;
                    if (UninstallWorkflow.ReadReceipt(Path.Combine(folder, ReceiptName)).State != "Failed") continue;
                    foreach (string entry in entries) File.Delete(entry);
                    Directory.Delete(folder, recursive: false);
                }
                catch { /* Best effort: a folder that cannot go stays, and the uninstall already succeeded. */ }
            }
        }

        private const int MoveFileDelayUntilReboot = 0x4;

        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existing, string? replacement, int flags);

        private static void ValidateOwned(string directory, string[] ownedFiles)
        {
            RejectLinks(directory);
            if (InstalledFileCleanup.OwnedPaths(directory, ownedFiles).Length != ownedFiles.Length)
                throw new System.Security.SecurityException("An owned uninstall path cannot be validated.");
        }

        private static void RejectLinks(string directory)
        {
            for (string? path = Path.GetFullPath(directory); path != null; path = Path.GetDirectoryName(path))
                if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new System.Security.SecurityException("Uninstall paths cannot pass through a reparse point.");
        }

        private static void CreateOperationDirectory(string directory, bool machine)
        {
            RejectLinks(Path.GetDirectoryName(directory)!);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("The uninstall operation directory already exists.");
            if (!machine)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
                if (!CreateDirectoryNative(directory, IntPtr.Zero))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return;
            }
            var security = new DirectorySecurity();
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.SetOwner(administrators);
            security.SetAccessRuleProtection(true, false);
            foreach (var pair in new[]
            {
                (administrators, FileSystemRights.FullControl),
                (new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl),
                (new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute)
            })
                security.AddAccessRule(new FileSystemAccessRule(pair.Item1, pair.Item2,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            byte[] descriptor = security.GetSecurityDescriptorBinaryForm();
            var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
            try
            {
                var attributes = new SecurityAttributes
                {
                    Length = Marshal.SizeOf(typeof(SecurityAttributes)),
                    Descriptor = pinned.AddrOfPinnedObject()
                };
                if (!CreateDirectorySecure(directory, ref attributes))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { pinned.Free(); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int Length;
            public IntPtr Descriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryNative(string path, IntPtr attributes);

        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectorySecure(string path, ref SecurityAttributes attributes);

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            using var algorithm = SHA256.Create();
            return Convert.ToBase64String(algorithm.ComputeHash(stream));
        }
    }
}
