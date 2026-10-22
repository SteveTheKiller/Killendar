using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Killendar.Services
{
    internal static class UpdateImagePublication
    {
        private static readonly ConcurrentDictionary<string, string> VerifiedHashes =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static string VerifiedHash(string path) =>
            VerifiedHashes.TryGetValue(Path.GetFullPath(path), out string? hash) ? hash
                : throw new InvalidDataException("This update has no retained verification hash. Download retained at: " + path);

        internal static void RememberVerified(string path, string sha256)
        {
            if (sha256.Length != 64 || Array.Exists(sha256.ToCharArray(), c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("The verified update hash is invalid.");
            VerifiedHashes[Path.GetFullPath(path)] = sha256;
        }

        /// <summary>The publication script, run by <see cref="UpdateHelperLaunch"/>. It opens the releases page when
        /// publication fails. <paramref name="overrides"/> replaces individual operations and exists for checks only.</summary>
        internal static string Script(string download, string target, bool elevated,
            string releasesUrl, string displayName, string? version = null, string? hive = null,
            string? reopen = null, string? overrides = null)
        {
            download = Path.GetFullPath(download);
            target = Path.GetFullPath(target);
            if (!VerifiedHashes.TryGetValue(download, out string? expected))
                throw new InvalidDataException("This update has no retained verification hash. Download retained at: " + download);
            string staged = target + ".new-" + Guid.NewGuid().ToString("N");
            string backup = target + ".old-" + Guid.NewGuid().ToString("N");
            string restore = target + ".restore-" + Guid.NewGuid().ToString("N");
            string report = download + ".recovery.txt";
            File.WriteAllText(report, "Update publication has not been confirmed. If the helper cannot run, retain these files."
                + Environment.NewLine + "Target: " + target + Environment.NewLine + "Download: " + download
                + Environment.NewLine + "Staging path (not yet verified): " + staged
                + Environment.NewLine + "Backup path: " + backup + Environment.NewLine + "Restoration path: " + restore
                + Environment.NewLine + "Expected SHA-256: " + expected);
            var script = new StringBuilder(Function);
            script.AppendLine("$plan = [pscustomobject]@{");
            Value(script, "Download", download); Value(script, "Target", target);
            Value(script, "Staged", staged); Value(script, "Backup", backup);
            Value(script, "Restore", restore);
            Value(script, "Report", report); Value(script, "Hash", expected);
            Value(script, "Releases", releasesUrl); Value(script, "DisplayName", displayName);
            Value(script, "Version", version ?? ""); Value(script, "Hive", hive ?? "");
            Value(script, "Reopen", reopen ?? "");
            Value(script, "Explorer", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            script.AppendLine("Elevated = " + (elevated ? "$true" : "$false")).AppendLine("}");
            script.AppendLine(Operations);
            if (overrides is not null) script.AppendLine(overrides);
            script.AppendLine("if (Invoke-UpdateImagePublication $plan $ops) { exit 0 } else { try { & $ops.Releases $plan.Releases } catch { }; exit 1 }");
            return script.ToString();
        }

        private static void Value(StringBuilder script, string name, string value) =>
            script.AppendLine(name + " = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"
                + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "'))");

        private const string Function = """
$ErrorActionPreference = 'Stop'
function Invoke-UpdateImagePublication {
    param($plan, $ops)
    $stageVerified = $false
    try {
        $bytes = & $ops.Read $plan.Download
        if ((& $ops.Hash $bytes) -ne $plan.Hash) { throw 'The download no longer matches its verified SHA-256.' }
        if (!(& $ops.VerifySignature $plan.Download)) { throw 'The downloaded update has no valid Authenticode signature.' }
        & $ops.Stage $plan.Staged $bytes
        if ((& $ops.Hash (& $ops.Read $plan.Staged)) -ne $plan.Hash) { throw 'The staged image failed SHA-256 verification.' }
        if (!(& $ops.VerifySignature $plan.Staged)) { throw 'The staged update has no valid Authenticode signature.' }
        $stageVerified = $true
        if (& $ops.Exists $plan.Target) { & $ops.Replace $plan.Staged $plan.Target $plan.Backup }
        else { & $ops.Move $plan.Staged $plan.Target }
        if ((& $ops.Hash (& $ops.Read $plan.Target)) -ne $plan.Hash) { throw 'The published image failed SHA-256 verification.' }
        & $ops.Metadata $plan
        & $ops.Relaunch $plan
    }
    catch {
        $failure = $_.Exception.ToString()
        $restoration = ''
        try {
            if (!(& $ops.Exists $plan.Target) -and (& $ops.Exists $plan.Backup)) {
                & $ops.Restore $plan.Backup $plan.Target $plan.Restore
            }
        }
        catch { $restoration = [Environment]::NewLine + 'Restoration failed: ' + $_.Exception.ToString() }
        $message = 'Update publication failed: ' + $failure + $restoration + [Environment]::NewLine +
            'Target: ' + $plan.Target + [Environment]::NewLine + 'Download: ' + $plan.Download +
            [Environment]::NewLine + 'Staging path (verified: ' + $stageVerified + '): ' + $plan.Staged +
            [Environment]::NewLine + 'Backup: ' + $plan.Backup + [Environment]::NewLine + 'Restoration path: ' + $plan.Restore +
            [Environment]::NewLine + 'Report: ' + $plan.Report
        try { & $ops.Report $plan.Report $message } catch { [Console]::Error.WriteLine($message) }
        return $false
    }
    foreach ($path in @($plan.Download, $plan.Staged, $plan.Backup, $plan.Restore, $plan.Report)) {
        try { & $ops.Delete $path } catch { }
    }
    return $true
}

""";
        private const string Operations = """
$ops = @{
    Read = { param($path) ,([IO.File]::ReadAllBytes($path)) }
    Hash = { param($bytes)
        $hash = [Security.Cryptography.SHA256]::Create()
        try { [BitConverter]::ToString($hash.ComputeHash([byte[]]$bytes)).Replace('-', '') }
        finally { $hash.Dispose() }
    }
    VerifySignature = { param($path)
        if (-not ('KillendarUpdateTrust' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
public static class KillendarUpdateTrust
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Size;
        public IntPtr Path, File, Subject;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr Policy, Sip;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData, Url;
        public uint ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, IntPtr data);
    public static bool IsValid(string path)
    {
        IntPtr name = IntPtr.Zero, file = IntPtr.Zero, data = IntPtr.Zero;
        try
        {
            using (var certificate = X509Certificate.CreateFromSignedFile(path)) { }
            name = Marshal.StringToHGlobalUni(path);
            file = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileInfo)));
            data = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustData)));
            Marshal.StructureToPtr(new FileInfo {
                Size = (uint)Marshal.SizeOf(typeof(FileInfo)), Path = name
            }, file, false);
            Marshal.StructureToPtr(new TrustData {
                Size = (uint)Marshal.SizeOf(typeof(TrustData)), UiChoice = 2,
                UnionChoice = 1, File = file, ProviderFlags = 0x1000
            }, data, false);
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            return WinVerifyTrust(IntPtr.Zero, ref action, data) == 0;
        }
        catch { return false; }
        finally
        {
            if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
            if (file != IntPtr.Zero) Marshal.FreeHGlobal(file);
            if (name != IntPtr.Zero) Marshal.FreeHGlobal(name);
        }
    }
}
'@
        }
        [KillendarUpdateTrust]::IsValid($path)
    }
    Stage = { param($path, $bytes)
        $stream = New-Object IO.FileStream($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
            [IO.FileShare]::None, 65536, [IO.FileOptions]::WriteThrough)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
        finally { $stream.Dispose() }
    }
    Exists = { param($path) [IO.File]::Exists($path) }
    Replace = { param($source, $target, $backup) [IO.File]::Replace($source, $target, $backup) }
    Move = { param($source, $target) [IO.File]::Move($source, $target) }
    Restore = { param($backup, $target, $restore)
        $bytes = & $ops.Read $backup
        $expected = & $ops.Hash $bytes
        & $ops.Stage $restore $bytes
        if ((& $ops.Hash (& $ops.Read $restore)) -ne $expected) { throw 'The restoration image failed SHA-256 verification.' }
        & $ops.Move $restore $target
    }
    Report = { param($path, $message) [IO.File]::WriteAllText($path, $message) }
    Releases = { param($url) Start-Process $url }
    Delete = { param($path) [IO.File]::Delete($path) }
    Metadata = { param($plan)
        if ($plan.Version.Length -eq 0 -or $plan.Hive.Length -eq 0) { return }
        if ($plan.Hive -eq 'HKLM') { $root = [Microsoft.Win32.Registry]::LocalMachine }
        elseif ($plan.Hive -eq 'HKCU') { $root = [Microsoft.Win32.Registry]::CurrentUser }
        else { throw 'The update registry scope is invalid.' }
        foreach ($keyPath in @(('Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $plan.DisplayName),
            ('Software\' + $plan.DisplayName))) {
            $key = $root.CreateSubKey($keyPath)
            try {
                if ($keyPath.Contains('Uninstall')) {
                    $key.SetValue('DisplayVersion', $plan.Version)
                    $key.SetValue('Publisher', 'Steve the Killer')
                }
                else { $key.SetValue('Version', $plan.Version) }
            }
            finally { if ($null -ne $key) { $key.Dispose() } }
        }
    }
    Relaunch = { param($plan)
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.UseShellExecute = $true
        if ($plan.Elevated) { $start.FileName = $plan.Explorer; $start.Arguments = '"' + $plan.Target + '"' }
        else {
            $start.FileName = $plan.Target
            if ($plan.Reopen.Length -gt 0) { $start.Arguments = '"' + $plan.Reopen + '"' }
        }
        $process = [Diagnostics.Process]::Start($start)
        if ($null -eq $process) { throw 'The updated application did not start.' }
        $process.Dispose()
    }
}
""";
    }
}
