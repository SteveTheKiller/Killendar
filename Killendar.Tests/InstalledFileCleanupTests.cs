using System;
using System.IO;
using KillerFamily.Deployment;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class InstalledFileCleanupTests
    {
        [Theory]
        [InlineData("..\\Work.kcal")]
        [InlineData("C:\\Work.kcal")]
        [InlineData("*.kcal")]
        public void InvalidManifestIsRejectedBeforeAnyOwnedFileIsRemoved(string invalid)
        {
            string root = Path.Combine(TestScratch.Root, "cleanup-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string exe = Path.Combine(root, "Killendar.exe");
                File.WriteAllText(exe, "owned executable fixture");

                Assert.Throws<ArgumentException>(() => InstalledFileCleanup.Remove(root, "Killendar.exe", invalid));

                Assert.Equal("owned executable fixture", File.ReadAllText(exe));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void OwnedFileCleanupPreservesCalendarsAndOtherUserFiles()
        {
            string scratch = Path.GetFullPath(TestScratch.Root);
            string root = Path.GetFullPath(Path.Combine(scratch, "cleanup-" + Guid.NewGuid().ToString("N")));
            Assert.StartsWith(scratch + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            try
            {
                File.WriteAllText(Path.Combine(root, "Killendar.exe"), "owned executable fixture");
                File.WriteAllText(Path.Combine(root, "sentinel.txt"), "user file");
                foreach (string path in new[] { Path.Combine(root, "Work.kcal"), Path.Combine(root, "Data", "Nested.kcal") })
                {
                    var store = new EventStore();
                    store.Open(path);
                    store.Add(new CalendarEvent { Title = "Preserved", Start = new DateTime(2026, 10, 20, 9, 0, 0),
                        End = new DateTime(2026, 10, 20, 10, 0, 0) });
                    store.Close();
                }
                byte[] original = File.ReadAllBytes(Path.Combine(root, "Work.kcal"));
                byte[] nested = File.ReadAllBytes(Path.Combine(root, "Data", "Nested.kcal"));

                InstalledFileCleanup.Remove(root, "Killendar.exe");

                Assert.True(File.Exists(Path.Combine(root, "Work.kcal")));
                Assert.Equal(original, File.ReadAllBytes(Path.Combine(root, "Work.kcal")));
                Assert.Equal(nested, File.ReadAllBytes(Path.Combine(root, "Data", "Nested.kcal")));
                Assert.Equal("user file", File.ReadAllText(Path.Combine(root, "sentinel.txt")));
                Assert.False(File.Exists(Path.Combine(root, "Killendar.exe")));
                foreach (string path in new[] { Path.Combine(root, "Work.kcal"), Path.Combine(root, "Data", "Nested.kcal") })
                {
                    var store = new EventStore();
                    try { store.Open(path); Assert.Equal("Preserved", Assert.Single(store.Events).Title); }
                    finally { store.Close(); }
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void DeferredCleanupContainsOnlyOwnedFileDeletesAndEmptyDirectoryRemoval()
        {
            string root = Path.Combine(TestScratch.Root, "cleanup-script-fixture");
            string batch = InstalledFileCleanup.RemovalBatch(root, "Killendar.exe");

            Assert.DoesNotContain("rmdir /s", batch);
            Assert.Contains("del /q \"" + Path.Combine(root, "Killendar.exe") + "\"", batch);
            Assert.Contains("rmdir \"" + root + "\"", batch);
        }
    }
}
