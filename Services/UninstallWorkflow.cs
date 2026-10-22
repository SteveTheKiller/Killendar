using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text.Json;

namespace Killendar.Services
{
    internal static class UninstallWorkflow
    {
        public const int Pending = 997;

        public sealed class Receipt
        {
            public string State { get; set; } = "Pending";
            public int ExitCode { get; set; } = Pending;
            public string Error { get; set; } = "";
            public string[] RemainingFiles { get; set; } = Array.Empty<string>();
            public string OperationId { get; set; } = "";
            public bool Machine { get; set; }
            public string TargetDirectory { get; set; } = "";
            public int ParentId { get; set; }
            public long ParentCreationFileTime { get; set; }
            public string ImageHash { get; set; } = "";
            public bool MetadataRemoved { get; set; }
            public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        }

        public static int StartDeferred(string receiptPath, Func<bool> launch, Receipt? receipt = null)
        {
            receipt ??= new Receipt();
            WriteReceipt(receiptPath, receipt);
            try
            {
                if (!launch()) throw new InvalidOperationException("The deferred uninstall process did not start.");
                return Pending;
            }
            catch (Exception error)
            {
                receipt.State = "Failed";
                receipt.ExitCode = KillerFamily.Deployment.InstallerExitCodes.FromException(error);
                receipt.Error = error.Message;
                WriteReceipt(receiptPath, receipt);
                return receipt.ExitCode;
            }
        }

        public static Receipt ReadReceipt(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Receipt>(reader.ReadToEnd())
                ?? throw new InvalidDataException("The uninstall receipt is empty.");
        }

        public static void WriteReceipt(string path, Receipt receipt)
        {
            receipt.UpdatedUtc = DateTime.UtcNow;
            string temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(receipt));
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        /// <summary>Runs a machine-wide uninstall elevated. The elevated copy shows its own
        /// confirmation, so it is waited for as long as that stays open.</summary>
        public static int? RequestElevation(bool machine, Func<bool> elevated,
            Func<Process?> launch, Action<Exception> report, int waitMilliseconds = Timeout.Infinite)
        {
            if (!machine) return null;
            try
            {
                if (elevated()) return null;
                using var child = launch();
                if (child == null) throw new InvalidOperationException("The administrator uninstall process did not start.");
                if (!child.WaitForExit(waitMilliseconds))
                    throw new System.ComponentModel.Win32Exception(KillerFamily.Deployment.InstallerExitCodes.Busy,
                        "The administrator uninstall process has not finished.");
                return child.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
            { return KillerFamily.Deployment.InstallerExitCodes.Cancelled; }
            catch (Exception error)
            {
                try { report(error); } catch { }
                return KillerFamily.Deployment.InstallerExitCodes.FromException(error);
            }
        }

        /// <summary>Removes the installed files. <paramref name="removeIntegration"/> (shortcuts, PATH,
        /// startup, file types) runs first, while the program is still in place, so if it fails the
        /// uninstall entry still runs a real program and can be tried again. <paramref name="removeMetadata"/>
        /// (the install and uninstall registration) runs last, once the files are gone.</summary>
        public static int CompleteDeferred(string directory, string[] ownedFiles, Action removeMetadata,
            int attempts = 30, int retryMilliseconds = 1000, Action<Exception>? report = null,
            Action? removeIntegration = null)
        {
            try
            {
                if (attempts < 1 || retryMilliseconds < 0) throw new ArgumentOutOfRangeException();
                string[] paths = KillerFamily.Deployment.InstalledFileCleanup.OwnedPaths(directory, ownedFiles);
                if (paths.Length != ownedFiles.Length)
                    return KillerFamily.Deployment.InstallerExitCodes.SecurityRejected;
                removeIntegration?.Invoke();
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    try
                    {
                        // Revalidate immediately before each removal attempt, including after a wait.
                        string[] current = KillerFamily.Deployment.InstalledFileCleanup.OwnedPaths(directory, ownedFiles);
                        if (current.Length != paths.Length)
                            return KillerFamily.Deployment.InstallerExitCodes.SecurityRejected;
                        foreach (string path in current) DeleteLiteral(path);
                        foreach (string path in current)
                            if (!Absent(path)) return KillerFamily.Deployment.InstallerExitCodes.Failure;
                        removeMetadata();
                        return KillerFamily.Deployment.InstallerExitCodes.Success;
                    }
                    catch (IOException error) when (attempt + 1 < attempts &&
                        KillerFamily.Deployment.InstallerExitCodes.FromException(error) == KillerFamily.Deployment.InstallerExitCodes.Busy)
                    { Thread.Sleep(retryMilliseconds); }
                }
            }
            catch (Exception error)
            {
                report?.Invoke(error);
                return KillerFamily.Deployment.InstallerExitCodes.FromException(error);
            }
            return KillerFamily.Deployment.InstallerExitCodes.Failure;
        }

        public static bool Absent(string path)
        {
            try { File.GetAttributes(path); return false; }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
        }

        public static void DeleteLiteral(string path)
        {
            if (Absent(path)) return;
            try { File.Delete(path); }
            catch (FileNotFoundException) { if (!Absent(path)) throw; }
            catch (DirectoryNotFoundException) { if (!Absent(path)) throw; }
            if (!Absent(path)) throw new IOException("An owned file remains after removal: " + path);
        }
    }
}
