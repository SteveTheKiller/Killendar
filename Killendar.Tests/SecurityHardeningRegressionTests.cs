using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class SecurityHardeningRegressionTests
    {
        // GB-001: the hash check and LoadLibrary must see the same file, so the verified handle
        // stays open (denying writers and deletion) while the loader runs.
        [Fact]
        public void TheVerifiedNativeStaysLockedWhileItIsLoaded()
        {
            string directory = Path.Combine(TestScratch.Root, "gb001-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "e_sqlcipher.dll");
            byte[] contents = { 1, 2, 3, 4 };
            try
            {
                File.WriteAllBytes(target, contents);
                byte[] hash;
                using (var sha = System.Security.Cryptography.SHA256.Create()) hash = sha.ComputeHash(contents);
                bool writeBlocked = false, deleteBlocked = false, loaded = false;

                SqlCipherBootstrap.LoadVerified(target, hash, path =>
                {
                    loaded = true;
                    try { File.WriteAllBytes(path, new byte[] { 9, 9 }); } catch (IOException) { writeBlocked = true; }
                    try { File.Delete(path); } catch (IOException) { deleteBlocked = true; }
                    return new IntPtr(1);
                });

                Assert.True(loaded);
                Assert.True(writeBlocked, "The file could be overwritten between the hash check and LoadLibrary.");
                Assert.True(deleteBlocked, "The file could be deleted or replaced between the hash check and LoadLibrary.");
                Assert.Equal(contents, File.ReadAllBytes(target));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public void ANativeThatDiffersFromTheEmbeddedCopyIsNeverLoaded()
        {
            string directory = Path.Combine(TestScratch.Root, "gb001-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "e_sqlcipher.dll");
            try
            {
                File.WriteAllBytes(target, new byte[] { 5, 6, 7 });
                byte[] hash;
                using (var sha = System.Security.Cryptography.SHA256.Create()) hash = sha.ComputeHash(new byte[] { 1, 2, 3 });
                bool loaded = false;

                Assert.Throws<IOException>(() => SqlCipherBootstrap.LoadVerified(target, hash, _ => { loaded = true; return new IntPtr(1); }));
                Assert.False(loaded);
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public void AFailedLoadSurfacesTheWin32Error()
        {
            string directory = Path.Combine(TestScratch.Root, "gb001-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "e_sqlcipher.dll");
            try
            {
                File.WriteAllBytes(target, new byte[] { 1 });
                byte[] hash;
                using (var sha = System.Security.Cryptography.SHA256.Create()) hash = sha.ComputeHash(new byte[] { 1 });
                Assert.Throws<Win32Exception>(() => SqlCipherBootstrap.LoadVerified(target, hash, _ => IntPtr.Zero));
            }
            finally { Directory.Delete(directory, true); }
        }

        // GB-002: a network logon of the same account must not reach the command pipe.
        [Fact]
        public void TheCommandPipeDeniesNetworkLogons()
        {
            using var server = CalendarCommandPipe.CreateServer("Killendar-test-" + Guid.NewGuid().ToString("N"));
            var rules = server.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<System.IO.Pipes.PipeAccessRule>().ToList();
            var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);

            Assert.Contains(rules, r => r.IdentityReference == network && r.AccessControlType == AccessControlType.Deny);
            Assert.DoesNotContain(rules, r => r.IdentityReference != network
                && r.IdentityReference != WindowsIdentity.GetCurrent().User);
            Assert.Contains(rules, r => r.IdentityReference == WindowsIdentity.GetCurrent().User
                && r.AccessControlType == AccessControlType.Allow);
        }

        [Fact]
        public void TheLaunchPipeDeniesNetworkLogons()
        {
            using var server = PipeGuard.CreateServer("Killendar-test-" + Guid.NewGuid().ToString("N"),
                System.IO.Pipes.PipeDirection.InOut);
            var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
            Assert.Contains(server.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<System.IO.Pipes.PipeAccessRule>(),
                r => r.IdentityReference == network && r.AccessControlType == AccessControlType.Deny);
        }

        // GB-003: the machine-wide install mutex carries an explicit DACL, so another local user
        // cannot hold or open the name through a default descriptor.
        [Fact]
        public void TheInstallMutexHasAnExplicitProtectedAcl()
        {
            string product = "KillendarAclTest" + Guid.NewGuid().ToString("N");
            using (KillerFamily.Deployment.InstallerExitCodes.Acquire(product))
            using (var opened = Mutex.OpenExisting(@"Global\KillerTools-Install-" + product, MutexRights.ReadPermissions))
            {
                var security = opened.GetAccessControl();
                Assert.True(security.AreAccessRulesProtected, "The DACL is inherited or defaulted instead of explicit.");
                var sids = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<MutexAccessRule>()
                    .Select(r => r.IdentityReference.Value).OrderBy(s => s).ToArray();
                var expected = new[]
                {
                    WindowsIdentity.GetCurrent().User!.Value,
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value,
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
                }.OrderBy(s => s).ToArray();
                Assert.Equal(expected, sids);
            }
        }

        // GB-004: the signer shown and compared comes from the verification itself. A file swapped
        // after WinVerifyTrust returns must not change who the verified signer is.
        [Fact]
        public void TheSignerComesFromTheVerificationState()
        {
            string signed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (!File.Exists(signed)) return;
            string directory = Path.Combine(TestScratch.Root, "gb004-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string copy = Path.Combine(directory, "app.exe");
            try
            {
                File.Copy(signed, copy);
                var plain = CodeSignature.Inspect(copy, null);
                Assert.True(plain.valid);

                var swapped = CodeSignature.Inspect(copy, () =>
                {
                    File.WriteAllBytes(copy, new byte[] { 0x4d, 0x5a, 0, 0 });
                });

                Assert.True(swapped.valid);
                Assert.Equal(plain.subject, swapped.subject);
                Assert.Equal(plain.thumb, swapped.thumb);
            }
            finally { Directory.Delete(directory, true); }
        }

        // GB-005: the tag handed to the download URL is the release's own spelling.
        [Theory]
        [InlineData("1.9.0", "1.9.0")]
        [InlineData("v1.9.0", "v1.9.0")]
        [InlineData("v1.9.00", "v1.9.00")]
        public void TheNewerReleaseTagIsReturnedAsPublished(string published, string expected)
        {
            string json = "{\"tag_name\":\"" + published + "\",\"draft\":false,\"prerelease\":false}";
            Assert.Equal(expected, UpdateService.NewerReleaseTag(json, new Version(1, 0, 0)));
        }
    }
}
