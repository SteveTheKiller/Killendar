using System;
using System.IO;
using KillerFamily.Deployment;
using Xunit;

namespace Killendar.Tests
{
    public sealed class InstalledImagePublicationTests
    {
        [Fact]
        public void PartialCopyFailurePreservesInstalledImage()
        {
            InScratch((source, target) =>
            {
                Assert.Throws<IOException>(() => InstalledImagePublication.Publish(source, target,
                    () => throw new Exception("Registration must not run."), (from, to) =>
                    {
                        File.WriteAllText(to, "partial");
                        throw new IOException("Injected interrupted copy.");
                    }));
                Assert.Equal("old complete image", File.ReadAllText(target));
                Assert.Equal("new complete image", File.ReadAllText(source));
            });
        }

        [Fact]
        public void RegistrationFailureRestoresInstalledImage()
        {
            InScratch((source, target) =>
            {
                Assert.Throws<IOException>(() => InstalledImagePublication.Publish(source, target,
                    () => throw new IOException("Injected registration failure.")));
                Assert.Equal("old complete image", File.ReadAllText(target));
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SuccessfulPublicationRegistersVerifiedImageThenRemovesBackup(bool firstInstall)
        {
            InScratch((source, target) =>
            {
                if (firstInstall) File.Delete(target);
                int registrations = 0;
                InstalledImagePublication.Publish(source, target, () =>
                {
                    Assert.Equal("new complete image", File.ReadAllText(target));
                    Assert.Equal(firstInstall ? 0 : 1,
                        Directory.GetFiles(Path.GetDirectoryName(target)!, "*.previous-*").Length);
                    registrations++;
                });
                Assert.Equal(1, registrations);
                Assert.Equal("new complete image", File.ReadAllText(target));
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.previous-*"));
            });
        }

        [Fact]
        public void ReplacementThatMovesOldImageThenFailsIsRecovered()
        {
            InScratch((source, target) =>
            {
                Assert.Throws<IOException>(() => InstalledImagePublication.Publish(source, target,
                    () => throw new Exception("Registration must not run."), replace: (staged, installed, backup) =>
                    {
                        File.Move(installed, backup);
                        throw new IOException("Injected partial replacement.");
                    }));
                Assert.Equal("old complete image", File.ReadAllText(target));
            });
        }

        [Fact]
        public void IncorrectStagedImageNeverReplacesInstalledImage()
        {
            InScratch((source, target) =>
            {
                Assert.Throws<IOException>(() => InstalledImagePublication.Publish(source, target,
                    () => throw new Exception("Registration must not run."),
                    (from, to) => File.WriteAllText(to, "incorrect bytes")));
                Assert.Equal("old complete image", File.ReadAllText(target));
            });
        }

        [Fact]
        public void FirstInstallRegistrationFailureRetainsCompleteCandidateWithoutInstalledImage()
        {
            InScratch((source, target) =>
            {
                File.Delete(target);
                Assert.Throws<IOException>(() => InstalledImagePublication.Publish(source, target,
                    () => throw new IOException("Injected registration failure.")));
                Assert.False(File.Exists(target));
                string candidate = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.install-*"));
                Assert.Equal("new complete image", File.ReadAllText(candidate));
                Assert.Equal("new complete image", File.ReadAllText(source));
            });
        }

        [Fact]
        public void PublishedHashMismatchRestoresVerifiedOldImage()
        {
            InScratch((source, target) =>
            {
                Assert.Throws<IOException>(() => InstalledImagePublication.Publish(source, target,
                    () => throw new Exception("Registration must not run."), replace: (staged, installed, backup) =>
                    {
                        File.Replace(staged, installed, backup);
                        File.WriteAllText(installed, "corrupt publication");
                    }));
                Assert.Equal("old complete image", File.ReadAllText(target));
            });
        }

        [Fact]
        public void LockedInstalledImageIsPreservedAndRegistrationDoesNotRun()
        {
            InScratch((source, target) =>
            {
                using (File.Open(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Assert.Throws<IOException>(() => InstalledImagePublication.Publish(source, target,
                        () => throw new Exception("Registration must not run.")));
                Assert.Equal("old complete image", File.ReadAllText(target));
            });
        }

        private static void InScratch(Action<string, string> exercise)
        {
            string root = Path.Combine(TestScratch.Root, "install-image-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "source.exe"), target = Path.Combine(root, "installed.exe");
                File.WriteAllText(source, "new complete image");
                File.WriteAllText(target, "old complete image");
                exercise(source, target);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
