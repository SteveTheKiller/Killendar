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
    }
}
