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
            bool asked = false;
            bool declined = InstallDowngrade.Accepted("run.exe", "inst.exe", p => versions[p], (_, _) => { asked = true; return false; });
            Assert.False(declined);
            Assert.True(asked);
            Assert.True(InstallDowngrade.Accepted("run.exe", "inst.exe", p => versions[p], (_, _) => true));
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
            Assert.True(InstallDowngrade.Accepted("run.exe", "inst.exe", p => versions[p], (_, _) => { asked = true; return false; }));
            Assert.False(asked);
        }
    }
}
