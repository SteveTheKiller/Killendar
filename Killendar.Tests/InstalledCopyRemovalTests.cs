using System;
using System.IO;
using Xunit;

namespace Killendar.Tests
{
    public sealed class InstalledCopyRemovalTests
    {
        [Fact]
        public void ALockedExeKeepsTheCopysRegistration()
        {
            string directory = NewInstallFolder();
            string exe = Path.Combine(directory, "Killendar.exe");
            bool registrationRemoved = false;
            try
            {
                bool removed;
                using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None))
                    removed = InstalledCopyRemoval.Remove(directory, ["Killendar.exe"], () => registrationRemoved = true);

                Assert.False(removed);
                Assert.False(registrationRemoved, "The registration went while the exe it describes stayed.");
                Assert.True(File.Exists(exe));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(directory)!, true);
            }
        }

        [Fact]
        public void TheFilesGoBeforeTheRegistration()
        {
            string directory = NewInstallFolder();
            string exe = Path.Combine(directory, "Killendar.exe");
            bool? exeWhenRegistrationRemoved = null;
            try
            {
                bool removed = InstalledCopyRemoval.Remove(directory, ["Killendar.exe"],
                    () => exeWhenRegistrationRemoved = File.Exists(exe));

                Assert.True(removed);
                Assert.Equal(false, exeWhenRegistrationRemoved);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(directory)!, true);
            }
        }

        private static string NewInstallFolder()
        {
            string directory = Path.Combine(TestScratch.Root, "copy-removal", Guid.NewGuid().ToString("N"), "Killendar");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Killendar.exe"), "MZ");
            return directory;
        }
    }
}
