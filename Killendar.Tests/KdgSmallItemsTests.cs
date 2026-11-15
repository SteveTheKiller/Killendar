using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    // KD-G: the console attach rule, save-name dots, the fallback brush, and the events.json
    // migration leftovers (null entries, the user-facing warning, a later-fixed file).
    [Collection("Calendar settings")]
    public sealed class KdgSmallItemsTests
    {
        [Theory]
        [InlineData(false, false, true)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, false)]
        public void ConsoleAttachesUnlessBothStreamsAreRedirected(bool output, bool error, bool expected) =>
            Assert.Equal(expected, CliConsole.ShouldAttach(output, error));

        [Theory]
        [InlineData(@"C:\out\report.", ".ics", @"C:\out\report.ics")]
        [InlineData(@"C:\out\report", ".ics", @"C:\out\report.ics")]
        [InlineData(@"C:\out\report.ICS", ".ics", @"C:\out\report.ICS")]
        public void ATrailingDotDoesNotDoubleUp(string path, string extension, string expected) =>
            Assert.Equal(expected, SaveFileNamePolicy.ApplyExtension(path, extension, true, false));

        [Fact]
        public void TheWhiteFallbackBrushIsFrozen()
        {
            TestApplication.Ensure();
            var brush = ThemeManager.ReadableOn(null, null);
            Assert.True(brush.IsFrozen);
        }

        [Fact]
        public void TheMigrationWarningReachesTheStatusLine()
        {
            using var profile = new Profile();
            File.WriteAllText(profile.Json, "[{\"Title\": \"Broken\",");
            var store = new EventStore();
            try
            {
                store.OpenActive();
                var calendar = new CalendarController(new EnglishCalendarHost(), store, null!);

                string? status = calendar.OpenStatus();

                Assert.NotNull(status);
                Assert.Contains("events.json", status);
                Assert.DoesNotContain("Str_", status);
            }
            finally { store.Close(); }
        }

        [Fact]
        public void ANullEntryInTheLegacyFileDoesNotBlockTheOpen()
        {
            using var profile = new Profile();
            File.WriteAllText(profile.Json, "[null, " + Entry("Kept") + "]");
            var store = new EventStore();
            try
            {
                store.OpenActive();

                Assert.Null(store.LoadError);
                Assert.True(store.IsOpen);
                Assert.Equal("Kept", Assert.Single(store.Events).Title);
            }
            finally { store.Close(); }
        }

        [Fact]
        public void AFixedLegacyFileIsRetriedOnTheNextLaunch()
        {
            using var profile = new Profile();
            File.WriteAllText(profile.Json, "[{\"Title\": \"Broken\",");
            var first = new EventStore();
            first.OpenActive();
            Assert.NotNull(first.MigrationWarning);
            Assert.Empty(first.Events);
            first.Close();
            SqliteConnection.ClearAllPools();

            File.WriteAllText(profile.Json, "[" + Entry("Recovered") + "]");
            var second = new EventStore();
            try
            {
                second.OpenActive();

                Assert.Null(second.LoadError);
                Assert.Null(second.MigrationWarning);
                Assert.Equal(1, second.MigratedCount);
                Assert.Equal("Recovered", Assert.Single(second.Events).Title);
                Assert.False(File.Exists(profile.Json));
            }
            finally { second.Close(); }
        }

        [Fact]
        public void AnInUseCalendarIsNeverReplacedByTheRetry()
        {
            using var profile = new Profile();
            File.WriteAllText(profile.Json, "[{\"Title\": \"Broken\",");
            var first = new EventStore();
            first.OpenActive();
            first.Add(new CalendarEvent { Title = "Mine", Start = new DateTime(2026, 10, 22, 9, 0, 0), End = new DateTime(2026, 10, 22, 10, 0, 0) });
            first.Close();
            SqliteConnection.ClearAllPools();

            File.WriteAllText(profile.Json, "[" + Entry("Recovered") + "]");
            var second = new EventStore();
            try
            {
                second.OpenActive();

                Assert.Equal("Mine", Assert.Single(second.Events).Title);
                Assert.True(File.Exists(profile.Json));
            }
            finally { second.Close(); }
        }

        // A parseable events.json whose migration fails after the work starts: the publish step is
        // refused because something that is not a file already sits at Default.kcal. Nothing partial
        // may appear, the legacy file stays where it is, the retry marker is written, and once the
        // obstacle is gone the next launch migrates every appointment.
        [Fact]
        public void AMigrationThatFailsPartwayPublishesNothingAndTheNextLaunchFinishesIt()
        {
            using var profile = new Profile();
            string target = Path.Combine(profile.Root, "Default.kcal");
            string text = "[" + Entry("First") + "," + Entry("Second") + "]";
            File.WriteAllText(profile.Json, text);
            Directory.CreateDirectory(target);
            var first = new EventStore();
            try
            {
                first.OpenActive();

                Assert.NotNull(first.LoadError);
                Assert.False(first.IsOpen);
                Assert.True(Directory.Exists(target));
                Assert.False(File.Exists(target));
                Assert.Equal(text, File.ReadAllText(profile.Json));
                Assert.False(File.Exists(profile.Json + ".migrated"));
                Assert.True(File.Exists(target + ".legacy-retry"));
                Assert.Empty(Directory.GetFileSystemEntries(profile.Root, "Default.kcal.migrate-*"));
            }
            finally { first.Close(); }
            SqliteConnection.ClearAllPools();

            Directory.Delete(target);
            var second = new EventStore();
            try
            {
                second.OpenActive();

                Assert.Null(second.LoadError);
                Assert.Equal(2, second.MigratedCount);
                Assert.Equal(new[] { "First", "Second" }, second.Events.Select(e => e.Title).OrderBy(t => t).ToArray());
                Assert.False(File.Exists(profile.Json));
                Assert.True(File.Exists(profile.Json + ".migrated"));
                Assert.False(File.Exists(target + ".legacy-retry"));
            }
            finally { second.Close(); }
        }

        private static string Entry(string title) =>
            "{\"Id\":\"" + Guid.NewGuid() + "\",\"Title\":\"" + title + "\",\"Start\":\"2026-10-22T09:00:00\",\"End\":\"2026-10-22T10:00:00\"}";

        private sealed class Profile : IDisposable
        {
            private static readonly FieldInfo Cache = typeof(Settings).GetField("_cache", BindingFlags.Static | BindingFlags.NonPublic)!;
            private readonly object? _previous;
            private readonly string _root;
            public string Json => Path.Combine(_root, "events.json");
            public string Root => _root;

            public Profile()
            {
                Assert.False(File.Exists(Path.Combine(AppPaths.LocalData, "Killendar", "events.json")),
                    "The suite profile must not hold a legacy source.");
                _root = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                _previous = Cache.GetValue(null);
                Cache.SetValue(null, new Dictionary<string, string> { ["KillendarDataDir"] = _root, ["ActiveKillendar"] = "Default.kcal" });
            }

            public void Dispose()
            {
                SqliteConnection.ClearAllPools();
                Cache.SetValue(null, _previous);
                try { Directory.Delete(_root, true); } catch (IOException) { }
            }
        }
    }
}
