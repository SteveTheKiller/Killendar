using System.Collections.Generic;
using Xunit;

namespace Killendar.Tests
{
    public sealed class InstallDowngradeTests
    {
        private static Dictionary<string, string?> Versions(string running, string installed)
            => new() { ["run.exe"] = running, ["inst.exe"] = installed };

        [Fact]
        public void AnOlderImageAsksTheDowngradeQuestionAndHonoursTheAnswer()
        {
            var versions = Versions("1.2.0.0", "1.3.0.0");
            string? asked = null;
            bool declined = InstallDowngrade.Accepted("run.exe", "inst.exe", p => versions[p], text => { asked = text; return false; });
            Assert.False(declined);
            Assert.Equal("You are about to install an older version (1.2.0.0) over the currently installed version (1.3.0.0).\n\nDowngrade anyway?", asked);
            Assert.True(InstallDowngrade.Accepted("run.exe", "inst.exe", p => versions[p], text => true));
        }

        [Theory]
        [InlineData("1.3.0.0", "1.3.0.0")]
        [InlineData("1.4.0.0", "1.3.0.0")]
        [InlineData("not a version", "1.3.0.0")]
        [InlineData("1.2.0.0", "")]
        public void NoQuestionIsAskedUnlessTheImageIsProvablyOlder(string running, string installed)
        {
            var versions = Versions(running, installed);
            bool asked = false;
            Assert.True(InstallDowngrade.Accepted("run.exe", "inst.exe", p => versions[p], text => { asked = true; return false; }));
            Assert.False(asked);
        }
    }
}
