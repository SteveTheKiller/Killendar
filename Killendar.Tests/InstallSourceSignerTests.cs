using System.Security;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The installer's signer check. 1.4.1 compared a first install against an installed
    /// copy that did not exist, read that as an unsigned development build, and refused Killendar's
    /// own signed installer with exit 1625, on every PC and with or without a network.</summary>
    public sealed class InstallSourceSignerTests
    {
        private const string Publisher = "Open Source Developer Stephen Riley";

        [Fact]
        public void AFirstInstallOfTheSignedReleaseIsAccepted()
            => App.CheckInstallSource(installedExists: false, (false, ""), (true, Publisher));

        [Fact]
        public void AFirstInstallOfAnUnsignedFileIsRefused()
            => Assert.Throws<SecurityException>(() => App.CheckInstallSource(false, (false, ""), (false, "(not signed)")));

        [Fact]
        public void AFirstInstallSignedBySomeoneElseIsRefused()
            => Assert.Throws<SecurityException>(() => App.CheckInstallSource(false, (false, ""), (true, "Fabrikam Inc")));

        [Fact]
        public void AnInstallOverTheSignedCopyIsAccepted()
            => App.CheckInstallSource(true, (true, Publisher), (true, Publisher));

        [Fact]
        public void AnInstallOverTheSignedCopyBySomeoneElseIsRefused()
            => Assert.Throws<SecurityException>(() => App.CheckInstallSource(true, (true, Publisher), (true, "Fabrikam Inc")));
    }
}
