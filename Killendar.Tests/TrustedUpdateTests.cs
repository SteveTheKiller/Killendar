using Killendar.Services;
using System;
using System.IO;
using System.Security;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Killendar.Tests
{
    public sealed class TrustedUpdateTests
    {
        [Fact]
        public void NonwritableUntrustedHostIsRejectedBeforeLaunch()
        {
            string path = Path.Combine(TestScratch.Root, "untrusted-update-" + Guid.NewGuid().ToString("N") + ".exe");
            UpdateImagePublication.RememberVerified(path, new string('A', 64), new string('B', 40));
            int launches = 0;
            var platform = new UpdateService.Platform { CanWrite = _ => false, Start = _ => { launches++; return null; } };
            try
            {
                Assert.Throws<SecurityException>(() => UpdateService.StartSwap(path, "9.0.0", platform));
                Assert.Equal(0, launches);
            }
            finally { if (File.Exists(path + ".recovery.txt")) File.Delete(path + ".recovery.txt"); }
        }

        [Fact]
        public void PerUserHelperReopensTheAppWhenItsGuardCannotCompile()
        {
            // An antivirus or locked-down PowerShell can refuse Add-Type. The per-user helper runs as
            // the same user as the app, so it falls back to a plain start of the installed copy.
            string path = Path.Combine(TestScratch.Root, "guard-fallback-" + Guid.NewGuid().ToString("N") + ".exe");
            UpdateImagePublication.RememberVerified(path, new string('A', 64), new string('B', 40));
            try
            {
                string script = UpdateImagePublication.Script(path, @"C:\Apps\Killendar.exe", false,
                    "https://example.invalid/releases", "Killendar");
                int guard = script.IndexOf("if (-not ('KillerUpdateGuard' -as [type]))", StringComparison.Ordinal);
                int wrapper = script.LastIndexOf("try {", guard, StringComparison.Ordinal);
                int fallback = script.IndexOf("Start-Process -FilePath $plan.Target", guard, StringComparison.Ordinal);
                Assert.True(guard > 0, "The guard is emitted.");
                Assert.True(wrapper >= 0 && script.IndexOf("$plan = ", StringComparison.Ordinal) < wrapper,
                    "The guard is wrapped after the plan exists.");
                Assert.True(fallback > guard, "A guard failure starts the installed copy.");
                Assert.True(fallback < script.IndexOf("Invoke-UpdateImagePublication $plan $ops", StringComparison.Ordinal),
                    "The fallback belongs to the guard, not to the publication.");
                Assert.Equal("0", ParseErrors(script));
            }
            finally { if (File.Exists(path + ".recovery.txt")) File.Delete(path + ".recovery.txt"); }
        }

        // Parses (never runs) a generated helper with Windows PowerShell's own parser and returns the error count.
        private static string ParseErrors(string script)
        {
            string file = Path.Combine(TestScratch.Root, "helper-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(file, script, Encoding.UTF8);
            try
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    "-NoLogo -NoProfile -NonInteractive -Command \"$e=$null;$null=[Management.Automation.Language.Parser]::ParseFile('"
                    + file.Replace("'", "''") + "',[ref]$null,[ref]$e);$e.Count\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var process = Process.Start(start)!;
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(30000);
                return output;
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void ElevatedHelperKeepsItsGuardUnwrapped()
        {
            // The elevated helper must never start the app as administrator, so it gets no plain fallback.
            string path = Path.Combine(TestScratch.Root, "guard-elevated-" + Guid.NewGuid().ToString("N") + ".exe");
            UpdateImagePublication.RememberVerified(path, new string('A', 64), new string('B', 40));
            try
            {
                string script = UpdateImagePublication.Script(path, @"C:\Apps\Killendar.exe", true,
                    "https://example.invalid/releases", "Killendar");
                Assert.DoesNotContain("Start-Process -FilePath $plan.Target", script);
            }
            finally { if (File.Exists(path + ".recovery.txt")) File.Delete(path + ".recovery.txt"); }
        }

        [Fact]
        public void RequestAcceptsOnlyCompleteAbsoluteData()
        {
            string[] args = { TrustedUpdate.HelperArgument, Convert.ToBase64String(Encoding.Unicode.GetBytes(@"C:\download.exe")),
                new string('A', 64), "9.0.0", "123", DateTime.UtcNow.Ticks.ToString(), Guid.NewGuid().ToString("N") };
            TrustedUpdate.ValidateRequest(args);
            args[4] = "-1";
            Assert.Throws<SecurityException>(() => TrustedUpdate.ValidateRequest(args));
            args[4] = "123";
            args[1] = Convert.ToBase64String(Encoding.Unicode.GetBytes("relative.exe"));
            Assert.Throws<SecurityException>(() => TrustedUpdate.ValidateRequest(args));
            Assert.Throws<SecurityException>(() => TrustedUpdate.ValidateRequest(Array.Empty<string>()));
        }

        [Fact]
        public void MachineDirectoryRequiresTrustedOwnerAndNoUserWrites()
        {
            var security = new DirectorySecurity();
            security.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"),
                FileSystemRights.FullControl, AccessControlType.Allow));
            TrustedUpdate.ValidateMachineOwner(security);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"),
                FileSystemRights.Write, AccessControlType.Allow));
            Assert.Throws<SecurityException>(() => TrustedUpdate.ValidateMachineOwner(security));
            var userOwned = new DirectorySecurity();
            userOwned.SetOwner(new SecurityIdentifier("S-1-5-32-545"));
            Assert.Throws<SecurityException>(() => TrustedUpdate.ValidateMachineOwner(userOwned));
        }
    }
}
