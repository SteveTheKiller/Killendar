using System;
using System.IO;
using Killendar.Services;
using Microsoft.Win32;
using Xunit;

namespace Killendar.Tests
{
    public sealed class FileAssociationsTests
    {
        [Theory]
        [InlineData("calendar.kcal")]
        [InlineData("invite.ics")]
        [InlineData("INVITE.ICS")]
        public void CalendarPathFromAcceptsSupportedExistingFiles(string fileName)
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarFileTests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, fileName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "test");

            try
            {
                Assert.Equal(path, FileAssociations.CalendarPathFrom([path]));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void CalendarPathFromRejectsUnsupportedAndMissingFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarFileTests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "calendar.txt");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "test");

            try
            {
                Assert.Null(FileAssociations.CalendarPathFrom([path]));
                Assert.Null(FileAssociations.CalendarPathFrom([Path.Combine(directory, "missing.ics")]));
                Assert.Null(FileAssociations.CalendarPathFrom([]));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void RegisterAddsIcsAsAnOpenWithChoiceWithoutTakingTheDefault()
        {
            string keyName = @"Software\Killendar.Tests\" + Guid.NewGuid().ToString("N");
            using RegistryKey root = Registry.CurrentUser.CreateSubKey(keyName)!;

            try
            {
                FileAssociations.Register(root, @"C:\Apps\Killendar.exe");

                using RegistryKey extension = root.OpenSubKey(@"Software\Classes\.ics")!;
                using RegistryKey openWith = extension.OpenSubKey("OpenWithProgids")!;
                using RegistryKey command = root.OpenSubKey(
                    @"Software\Classes\" + FileAssociations.IcsProgId + @"\shell\open\command")!;
                using RegistryKey supported = root.OpenSubKey(
                    @"Software\Classes\Applications\Killendar.exe\SupportedTypes")!;

                Assert.Null(extension.GetValue(""));
                Assert.NotNull(openWith.GetValue(FileAssociations.IcsProgId));
                Assert.Equal("\"C:\\Apps\\Killendar.exe\" \"%1\"", command.GetValue(""));
                Assert.NotNull(supported.GetValue(".ics"));

                FileAssociations.Unregister(root);
                Assert.Null(root.OpenSubKey(@"Software\Classes\" + FileAssociations.IcsProgId));
                Assert.Null(openWith.GetValue(FileAssociations.IcsProgId));
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            }
        }

        [Fact]
        public void UnregisterDeletesTheEmptyExtensionKeyItBlanked()
        {
            WithScratchRoot(root =>
            {
                FileAssociations.Register(root, @"C:\Apps\Killendar.exe");
                FileAssociations.Unregister(root);
                Assert.Null(root.OpenSubKey(@"Software\Classes\" + EventStore.Extension));
            });
        }

        [Fact]
        public void UnregisterLeavesTheExtensionKeyWhenAnotherProgIdOwnsIt()
        {
            WithScratchRoot(root =>
            {
                using (var k = root.CreateSubKey(@"Software\Classes\" + EventStore.Extension))
                    k.SetValue("", "Other.Calendar");
                FileAssociations.Unregister(root);
                using var kept = root.OpenSubKey(@"Software\Classes\" + EventStore.Extension)!;
                Assert.Equal("Other.Calendar", kept.GetValue(""));
            });
        }

        [Fact]
        public void UnregisterLeavesTheExtensionKeyWhenItHoldsSubkeysOrOtherValues()
        {
            WithScratchRoot(root =>
            {
                FileAssociations.Register(root, @"C:\Apps\Killendar.exe");
                using (var k = root.CreateSubKey(@"Software\Classes\" + EventStore.Extension + @"\OpenWithProgids"))
                    k.SetValue("Other.Calendar", "");
                FileAssociations.Unregister(root);
                using var kept = root.OpenSubKey(@"Software\Classes\" + EventStore.Extension)!;
                Assert.Equal("", kept.GetValue(""));
                Assert.NotNull(kept.OpenSubKey("OpenWithProgids"));

                using (var k = root.CreateSubKey(@"Software\Classes\.other"))
                    k.SetValue("Content Type", "text/x-other");
                using (var k = root.CreateSubKey(@"Software\Classes\" + EventStore.Extension))
                    k.SetValue("", FileAssociations.ProgId);
                root.DeleteSubKeyTree(@"Software\Classes\" + EventStore.Extension + @"\OpenWithProgids");
                using (var k = root.OpenSubKey(@"Software\Classes\" + EventStore.Extension, true)!)
                    k.SetValue("Content Type", "text/x-kcal");
                FileAssociations.Unregister(root);
                using var valued = root.OpenSubKey(@"Software\Classes\" + EventStore.Extension)!;
                Assert.Equal("text/x-kcal", valued.GetValue("Content Type"));
            });
        }

        [Fact]
        public void AMachineRegistrationClearsTheUserShadowAPortableLaunchWrote()
        {
            WithScratchRoot(machine => WithScratchRoot(user =>
            {
                FileAssociations.Register(user, @"D:\Downloads\Killendar.exe");

                FileAssociations.RegisterMachine(machine, user, @"C:\Program Files\Killendar\Killendar.exe");

                using (var command = machine.OpenSubKey(@"Software\Classes\" + FileAssociations.ProgId + @"\shell\open\command")!)
                    Assert.Equal("\"C:\\Program Files\\Killendar\\Killendar.exe\" \"%1\"", command.GetValue(""));
                Assert.Null(user.OpenSubKey(@"Software\Classes\" + FileAssociations.ProgId));
                Assert.Null(user.OpenSubKey(@"Software\Classes\" + EventStore.Extension));
                using var openWith = user.OpenSubKey(@"Software\Classes\.ics\OpenWithProgids");
                Assert.Null(openWith?.GetValue(FileAssociations.IcsProgId));
            }));
        }

        [Fact]
        public void AMachineRegistrationLeavesAnotherAppsUserCalendarDefault()
        {
            WithScratchRoot(machine => WithScratchRoot(user =>
            {
                using (var k = user.CreateSubKey(@"Software\Classes\" + EventStore.Extension))
                    k.SetValue("", "Other.Calendar");

                FileAssociations.RegisterMachine(machine, user, @"C:\Program Files\Killendar\Killendar.exe");

                using var kept = user.OpenSubKey(@"Software\Classes\" + EventStore.Extension)!;
                Assert.Equal("Other.Calendar", kept.GetValue(""));
            }));
        }

        private static void WithScratchRoot(Action<RegistryKey> exercise)
        {
            string keyName = @"Software\Killendar.Tests\" + Guid.NewGuid().ToString("N");
            using RegistryKey root = Registry.CurrentUser.CreateSubKey(keyName)!;
            try { exercise(root); }
            finally { Registry.CurrentUser.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false); }
        }
    }
}
