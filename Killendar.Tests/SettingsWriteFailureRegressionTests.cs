using System;
using System.IO;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // SS-007: renaming the open Killendar while settings.json cannot be written must not leave the
    // next launch pointing at the old name, where it would create an empty calendar.
    [Collection("Calendar settings")]
    public sealed class SettingsWriteFailureRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, "settings-write", Guid.NewGuid().ToString("N"));
        private readonly string? _savedDir = Settings.Get("KillendarDataDir");
        private readonly string? _savedActive = Settings.Get("ActiveKillendar");

        public SettingsWriteFailureRegressionTests()
        {
            Directory.CreateDirectory(_root);
            EventStore.SetDataDir(_root);
            EventStore.LastRename = null;
        }

        public void Dispose()
        {
            EventStore.LastRename = null;
            Settings.Set("KillendarDataDir", _savedDir ?? "");
            Settings.Set("ActiveKillendar", _savedActive ?? "");
            Directory.Delete(_root, true);
        }

        [Fact]
        public void RenameThatCannotRetargetTheActiveSettingIsUndone()
        {
            string a = Path.Combine(_root, "A.kcal");
            File.WriteAllBytes(a, []);
            EventStore.SetActive("A.kcal");

            using (new FileStream(Settings.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<SettingsWriteException>(() => EventStore.RenameKillendar("A.kcal", "B.kcal"));

            Assert.True(File.Exists(a));
            Assert.False(File.Exists(Path.Combine(_root, "B.kcal")));
            Assert.Equal("A.kcal", EventStore.ActiveFile);
            Assert.Null(EventStore.LastRename);
        }

        [Fact]
        public void DataFolderThatCannotBeSavedIsReportedAndNotHalfApplied()
        {
            string other = Path.Combine(_root, "Other");
            Directory.CreateDirectory(other);
            EventStore.SetActive("A.kcal");

            using (new FileStream(Settings.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<SettingsWriteException>(() => EventStore.SetDataDir(other));

            Assert.Equal(Path.GetFullPath(_root), EventStore.DataDir);
            Assert.Equal("A.kcal", EventStore.ActiveFile);
        }

        [Fact]
        public void FailedSettingsWriteSaysSo()
        {
            using (new FileStream(Settings.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.False(Settings.TrySet("ActiveKillendar", "Elsewhere.kcal"));
            Assert.True(Settings.TrySet("ActiveKillendar", "Elsewhere.kcal"));
        }
    }
}
