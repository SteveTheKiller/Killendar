using System;
using System.IO;
using System.Reflection;
using System.Security;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // The updater's trust rule, at the seams that take the signer subjects and versions directly.
    // UpdateTrustRegressionTests holds the publisher-name and revocation cases; these are the rest of
    // the accept and reject shapes.
    public sealed class ReleaseIdentityTests
    {
        private const string Publisher = "Open Source Developer Stephen Riley";
        private const string PinnedThumbprint = "E478E4940DFD3547DAF2199494B399214FB3E0FD";
        private static readonly (bool valid, string subject) Unsigned = (false, "(not signed)");

        // Name is set as a property, never passed to the constructor. On .NET Framework the
        // constructor parses a display name through Fusion, which hands back the casing it saw
        // first: once the "killendar" case below has run, new AssemblyName("Killendar") comes back
        // lowercase, and the newer-release cases fail on test order alone.
        private static AssemblyName Incoming(string name, Version? version) => new() { Name = name, Version = version };

        // ---- VerifySigner ----

        [Fact]
        public void UnsignedDevelopmentBuildAcceptsTheSignerItIsPinnedTo()
        {
            ReleaseIdentity.VerifySigner(Unsigned, (true, Publisher), requireInstalledSignature: false, PinnedThumbprint);
        }

        [Fact]
        public void ThePinnedThumbprintMatchesInEitherCase()
        {
            ReleaseIdentity.VerifySigner(Unsigned, (true, Publisher), requireInstalledSignature: false, PinnedThumbprint.ToLowerInvariant());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0000000000000000000000000000000000000000")]
        [InlineData("E478E4940DFD3547DAF2199494B399214FB3E0FE")]
        public void UnsignedDevelopmentBuildRefusesAnyOtherThumbprint(string? thumbprint)
        {
            var error = Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.VerifySigner(Unsigned, (true, Publisher), requireInstalledSignature: false, thumbprint));
            Assert.Equal("The update signer is not trusted by this development build.", error.Message);
        }

        [Fact]
        public void SignedCopyAcceptsARenewedCertificateWithADifferentThumbprint()
        {
            ReleaseIdentity.VerifySigner((true, Publisher), (true, Publisher), requireInstalledSignature: false,
                "1111111111111111111111111111111111111111");
        }

        [Fact]
        public void SignedCopyComparesPublishersWithoutRegardToCase()
        {
            ReleaseIdentity.VerifySigner((true, Publisher.ToUpperInvariant()), (true, Publisher), requireInstalledSignature: true);
        }

        [Fact]
        public void SignedCopyRefusesAReleasePublisherWhenItsOwnPublisherDiffers()
        {
            var error = Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.VerifySigner((true, "Fabrikam Inc"), (true, Publisher), requireInstalledSignature: false));
            Assert.Equal("The update is not signed by the publisher of the installed copy.", error.Message);
        }

        [Fact]
        public void IncomingFileWithoutAValidSignatureIsRefused()
        {
            var error = Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.VerifySigner((true, Publisher), (false, Publisher), requireInstalledSignature: false, PinnedThumbprint));
            Assert.Equal("The update has no valid Authenticode signature.", error.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("(not signed)")]
        [InlineData("Fabrikam Inc")]
        [InlineData("Stephen Riley")]
        public void IncomingSignatureFromAnyoneButThePublisherIsRefused(string subject)
        {
            var error = Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.VerifySigner((true, Publisher), (true, subject), requireInstalledSignature: false, PinnedThumbprint));
            Assert.Equal("The update is not signed by the publisher.", error.Message);
        }

        [Fact]
        public void ElevatedPathRefusesAnInstalledCopyWithoutAValidSignature()
        {
            var error = Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.VerifySigner(Unsigned, (true, Publisher), requireInstalledSignature: true, PinnedThumbprint));
            Assert.Equal("The installed update helper has no valid signature.", error.Message);
        }

        // ---- Verify: product and version ----

        [Theory]
        [InlineData("9.0.0")]
        [InlineData("v9.0.0")]
        [InlineData("V9.0.0")]
        [InlineData("9.0.0.0")]
        public void ANewerReleaseIsAcceptedInEveryVersionSpelling(string offered)
        {
            ReleaseIdentity.Verify(Incoming("Killendar", new Version(9, 0, 0, 0)), offered, new Version(1, 4, 1),
                (true, Publisher), (true, Publisher), requireInstalledSignature: true);
        }

        [Theory]
        [InlineData(1, 4, 1)]
        [InlineData(1, 4, 0)]
        [InlineData(0, 9, 9)]
        public void AReleaseThatIsNotNewerIsRefused(int major, int minor, int build)
        {
            string offered = major + "." + minor + "." + build;
            var error = Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.Verify(Incoming("Killendar", new Version(major, minor, build, 0)), offered, new Version(1, 4, 1),
                    (true, Publisher), (true, Publisher), requireInstalledSignature: true));
            Assert.Equal("The update's product or release version does not match a newer Killendar release.", error.Message);
        }

        [Fact]
        public void AnAssemblyWhoseVersionIsNotTheOfferedOneIsRefused()
        {
            Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.Verify(Incoming("Killendar", new Version(2, 0, 0, 0)), "9.0.0", new Version(1, 4, 1),
                    (true, Publisher), (true, Publisher), requireInstalledSignature: true));
        }

        [Fact]
        public void AnAssemblyWithoutAVersionIsRefused()
        {
            Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.Verify(Incoming("Killendar", null), "9.0.0", new Version(1, 4, 1),
                    (true, Publisher), (true, Publisher), requireInstalledSignature: true));
        }

        [Theory]
        [InlineData("OtherApp")]
        [InlineData("killendar")]
        public void AnAssemblyOfAnotherProductIsRefused(string product)
        {
            Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.Verify(Incoming(product, new Version(9, 0, 0, 0)), "9.0.0", new Version(1, 4, 1),
                    (true, Publisher), (true, Publisher), requireInstalledSignature: true));
        }

        [Fact]
        public void TheSignerIsCheckedBeforeTheVersion()
        {
            var error = Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.Verify(Incoming("Killendar", new Version(1, 0, 0, 0)), "1.0.0", new Version(1, 4, 1),
                    (true, Publisher), (true, "Fabrikam Inc"), requireInstalledSignature: true));
            Assert.Equal("The update is not signed by the publisher.", error.Message);
        }

        // ---- NormalizeVersion and VerifyPackageVersion ----

        [Theory]
        [InlineData("1.4.1", "1.4.1")]
        [InlineData("v1.4.1", "1.4.1")]
        [InlineData(" V1.4.1 ", "1.4.1")]
        [InlineData("1.4.1.0", "1.4.1")]
        [InlineData("10.20.30", "10.20.30")]
        public void VersionsNormalizeToThreeParts(string text, string expected)
        {
            Assert.Equal(expected, ReleaseIdentity.NormalizeVersion(text));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.4")]
        [InlineData("latest")]
        [InlineData("1.4.x")]
        public void AVersionWithoutThreeNumbersIsRefused(string? text)
        {
            var error = Assert.Throws<SecurityException>(() => ReleaseIdentity.NormalizeVersion(text));
            Assert.Equal("The release version is invalid.", error.Message);
        }

        [Fact]
        public void APackageWhoseFileVersionIsTheNewerOfferedOneIsAccepted()
        {
            ReleaseIdentity.VerifyPackageVersion(new Version(9, 0, 0, 0), "v9.0.0", new Version(1, 4, 1, 0));
        }

        [Theory]
        [InlineData(8, 0, 0, "9.0.0")]
        [InlineData(1, 4, 1, "1.4.1")]
        [InlineData(1, 3, 0, "1.3.0")]
        public void APackageThatDoesNotMatchANewerReleaseIsRefused(int major, int minor, int build, string offered)
        {
            var error = Assert.Throws<SecurityException>(() =>
                ReleaseIdentity.VerifyPackageVersion(new Version(major, minor, build, 0), offered, new Version(1, 4, 1, 0)));
            Assert.Equal("The update package's version does not match a newer Killendar release.", error.Message);
        }

        // ---- RequireLocalFile ----

        [Fact]
        public void AnOrdinaryFileOnALocalDriveIsAccepted()
        {
            string folder = Path.Combine(TestScratch.Root, "local-file-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string file = Path.Combine(folder, "update.exe");
                File.WriteAllText(file, "image");

                Assert.Equal(Path.GetFullPath(file), ReleaseIdentity.RequireLocalFile(file));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void AFolderIsRefused()
        {
            string folder = Path.Combine(TestScratch.Root, "local-folder-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var error = Assert.Throws<SecurityException>(() => ReleaseIdentity.RequireLocalFile(folder));
                Assert.Equal("The update download must be an ordinary file.", error.Message);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Theory]
        [InlineData(@"\\server\share\update.exe")]
        [InlineData(@"\\?\C:\update.exe")]
        public void ANetworkOrDevicePathIsRefused(string path)
        {
            var error = Assert.Throws<SecurityException>(() => ReleaseIdentity.RequireLocalFile(path));
            Assert.Equal("The update download must be a file on a local drive.", error.Message);
        }
    }
}
