using System.Collections.Generic;
using Xunit;

namespace Killendar.Tests
{
    public sealed class AllUsersInstallTests
    {
        private const string Desktop = @"C:\Users\pat\Desktop\Killendar.lnk";
        private const string MachineExe = @"C:\Program Files\Killendar\Killendar.exe";

        // The desktop link is shared by both scopes now: removing the per-user copy (the twice-
        // installed repair run from the machine copy) must not take a link that opens the machine copy.
        [Theory]
        [InlineData(@"C:\Users\pat\AppData\Local\Programs\Killendar\Killendar.exe", true)]
        [InlineData(@"c:\users\PAT\appdata\local\programs\killendar\KILLENDAR.EXE", true)]
        [InlineData(MachineExe, false)]
        [InlineData(null, false)]
        public void ThePerUserRemovalTakesOnlyItsOwnDesktopLink(string? target, bool removes)
        {
            Assert.Equal(removes, AllUsersInstall.PerUserOwnsDesktopLink(target,
                @"C:\Users\pat\AppData\Local\Programs\Killendar\Killendar.exe"));
        }

        [Fact]
        public void TheDesktopShortcutIsMadeForTheMachineCopyAfterThePerUserCopyIsRemoved()
        {
            var calls = new List<string>();

            bool installed = AllUsersInstall.Run(true, Desktop, MachineExe,
                () => { calls.Add("elevated"); return true; },
                () => calls.Add("remove per-user"),
                (link, target) => calls.Add("shortcut " + link + " -> " + target),
                () => { calls.Add("relaunch"); return true; });

            Assert.True(installed);
            Assert.Equal(new[]
            {
                "elevated",
                "remove per-user",
                @"shortcut C:\Users\pat\Desktop\Killendar.lnk -> C:\Program Files\Killendar\Killendar.exe",
                "relaunch",
            }, calls);
        }

        [Fact]
        public void NoDesktopShortcutWhenTheBoxIsCleared()
        {
            var calls = new List<string>();

            AllUsersInstall.Run(false, Desktop, MachineExe,
                () => true, () => calls.Add("remove per-user"),
                (link, target) => calls.Add("shortcut"), () => { calls.Add("relaunch"); return true; });

            Assert.Equal(new[] { "remove per-user", "relaunch" }, calls);
        }

        [Fact]
        public void ADeclinedElevationChangesNothing()
        {
            var calls = new List<string>();

            bool installed = AllUsersInstall.Run(true, Desktop, MachineExe,
                () => false, () => calls.Add("remove per-user"),
                (link, target) => calls.Add("shortcut"), () => { calls.Add("relaunch"); return true; });

            Assert.False(installed);
            Assert.Empty(calls);
        }
    }
}
