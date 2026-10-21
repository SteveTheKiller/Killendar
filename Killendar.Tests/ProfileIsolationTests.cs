using System;
using System.IO;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public class ProfileIsolationTests
    {
        [Fact]
        public void PerUserStateResolvesUnderTheTestProfile()
        {
            string profile = TestScratch.Profile + Path.DirectorySeparatorChar;

            Assert.StartsWith(profile, Settings.FilePath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(profile, SqlCipherBootstrap.NativeDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(profile, App.CrashLogPath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(profile, EventStore.DefaultDataDir, StringComparison.OrdinalIgnoreCase);
        }

        // The variable itself is never cleared here: other tests run in parallel and must keep
        // resolving into the scratch profile.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("relative\\path")]
        public void UnusableProfileRootFallsBackToTheWindowsFolder(string? root)
        {
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppPaths.Resolve(root, "Local", Environment.SpecialFolder.LocalApplicationData));
        }

        [Fact]
        public void ProfileRootSeparatesLocalAndRoamingState()
        {
            string root = Path.Combine(TestScratch.Root, "resolve-check");
            Assert.Equal(Path.Combine(root, "Local"),
                AppPaths.Resolve(root, "Local", Environment.SpecialFolder.LocalApplicationData));
            Assert.Equal(Path.Combine(root, "Roaming"),
                AppPaths.Resolve(root, "Roaming", Environment.SpecialFolder.ApplicationData));
        }
    }
}
