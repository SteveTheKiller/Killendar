using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class SettingsRecoveryTests
    {
        [Theory]
        [InlineData(".tmp", false)]
        [InlineData(".bak", false)]
        [InlineData(".bak", true)]
        public void MissingOrDamagedPrimaryRecoversCalendarLocation(string suffix, bool damaged)
        {
            using var profile = new Profile();
            File.WriteAllText(profile.Path + suffix, "{\"ActiveKillendar\":\"C:\\\\Calendars\\\\Work.kcal\"}");
            if (damaged) File.WriteAllText(profile.Path, "{");
            Assert.Equal(@"C:\Calendars\Work.kcal", Settings.Get("ActiveKillendar"));
        }

        [Fact]
        public void FailedPublicationKeepsThePreviousDictionaryAndSuccessfulReplacementRetainsBackup()
        {
            using var profile = new Profile();
            Settings.Set("ActiveKillendar", "Original.kcal");
            using (new FileStream(profile.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Settings.Set("ActiveKillendar", "Unpublished.kcal");
                Assert.Equal("Original.kcal", Settings.Get("ActiveKillendar"));
            }
            profile.Reload();
            Assert.Equal("Original.kcal", Settings.Get("ActiveKillendar"));
            Settings.Set("ActiveKillendar", "New.kcal");
            Assert.Contains("Original.kcal", File.ReadAllText(profile.Path + ".bak"));
            profile.Reload();
            Assert.Equal("New.kcal", Settings.Get("ActiveKillendar"));
        }

        private sealed class Profile : IDisposable
        {
            private readonly string _root = System.IO.Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly object? _oldDir, _oldPath, _oldCache;
            public string Path => System.IO.Path.Combine(_root, "settings.json");
            public Profile()
            {
                Directory.CreateDirectory(_root);
                _oldDir = _dir.GetValue(null); _oldPath = _path.GetValue(null); _oldCache = _cache.GetValue(null);
                _dir.SetValue(null, _root); _path.SetValue(null, Path); Reload();
            }
            public void Reload() => _cache.SetValue(null, null);
            public void Dispose()
            {
                _dir.SetValue(null, _oldDir); _path.SetValue(null, _oldPath); _cache.SetValue(null, _oldCache);
                Directory.Delete(_root, true);
            }
        }
    }
}
