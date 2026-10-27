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
