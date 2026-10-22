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
            UpdateImagePublication.RememberVerified(path, new string('A', 64));
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
        public void ScriptLauncherCannotRequestElevation()
        {
            var start = UpdateHelperLaunch.StartInfo("exit 0");
            Assert.False(start.UseShellExecute);
            Assert.True(start.CreateNoWindow);
            Assert.True(string.IsNullOrEmpty(start.Verb));
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ProtectedPublicationWaitsAndRejectsChangedOriginal(bool changeOriginal)
        {
            string root = Path.Combine(TestScratch.Root, "trusted-publication-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string target = Path.Combine(root, "Killendar.exe"), staged = Path.Combine(root, "staged.exe");
            byte[] original = Encoding.UTF8.GetBytes("original"), replacement = Encoding.UTF8.GetBytes("replacement");
            File.WriteAllBytes(target, original);
            File.WriteAllBytes(staged, replacement);
            UpdateImagePublication.RememberVerified(staged, Hash(replacement));
            using var parent = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                "/d /c ping -n 4 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true });
            Assert.NotNull(parent);
            try
            {
                if (changeOriginal) File.WriteAllBytes(target, Encoding.UTF8.GetBytes("changed"));
                string overrides = "$ops.Metadata = { param($plan) }\n$ops.Relaunch = { param($plan) }\n"
                    + "$ops.VerifySignature = { param($path) $true }\n$ops.Releases = { param($url) }";
                using var process = Process.Start(TrustedUpdate.Publication(staged, target, "9.0.0", Hash(original),
                    parent!.Id, parent.StartTime.ToUniversalTime().Ticks,
                    parent.Id, parent.StartTime.ToUniversalTime().Ticks, overrides));
                Assert.NotNull(process);
                Assert.False(process!.WaitForExit(250));
                Assert.Equal(changeOriginal ? Encoding.UTF8.GetBytes("changed") : original, File.ReadAllBytes(target));
                Assert.True(process.WaitForExit(60000));
                Assert.True(process.ExitCode == (changeOriginal ? 1 : 0), File.Exists(staged + ".recovery.txt")
                    ? File.ReadAllText(staged + ".recovery.txt") : "Exit " + process.ExitCode);
                Assert.Equal(changeOriginal ? Encoding.UTF8.GetBytes("changed") : replacement, File.ReadAllBytes(target));
            }
            finally
            {
                if (!parent!.HasExited) { parent.Kill(); parent.WaitForExit(); }
                Directory.Delete(root, true);
            }
        }

        private static string Hash(byte[] bytes)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }
    }
}
