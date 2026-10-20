using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class LegacyMigrationFailureTests
    {
        [Fact]
        public void IncompleteMigrationIsNotPublishedAndCorrectedInputCanRetry()
        {
            // Redirect the profile settings boundary in memory. Never write the real profile.
            Assert.False(File.Exists(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Killendar", "events.json")),
                "Run this migration fixture in an isolated profile without a legacy source.");
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var cache = typeof(Settings).GetField("_cache", BindingFlags.Static | BindingFlags.NonPublic)!;
            object? previous = cache.GetValue(null);
            cache.SetValue(null, new Dictionary<string, string> { ["KillendarDataDir"] = root, ["ActiveKillendar"] = "Default.kcal" });
            var store = new EventStore();
            string json = Path.Combine(root, "events.json");
            string target = Path.Combine(root, "Default.kcal");
            var timed = new CalendarEvent { Title = "Timed", Start = new DateTime(2026, 1, 5, 15, 30, 0), End = new DateTime(2026, 1, 5, 16, 30, 0) };
            var allDay = new CalendarEvent { Title = "All day", AllDay = true, Start = new DateTime(2026, 1, 6), End = new DateTime(2026, 1, 7) };
            try
            {
                var options = new JsonSerializerOptions { IgnoreReadOnlyProperties = true };
                string broken = JsonSerializer.Serialize(new CalendarEvent?[] { timed, null }, options);
                File.WriteAllText(json, broken);
                store.Prepare();
                Assert.NotNull(store.LoadError);
                Assert.False(File.Exists(target));
                Assert.Equal(broken, File.ReadAllText(json));

                store.OpenActive();
                Assert.NotNull(store.LoadError);
                Assert.False(store.IsOpen);
                Assert.False(File.Exists(target));

                File.WriteAllText(json, JsonSerializer.Serialize(new[] { timed, allDay }, options));
                store.Prepare();
                Assert.Null(store.LoadError);
                Assert.Equal(2, store.MigratedCount);
                Assert.True(File.Exists(json + ".migrated"));
                store.Open(target);
                Assert.Collection(store.Events,
                    e => { Assert.Equal("Timed", e.Title); Assert.Equal(timed.Start, e.Start); Assert.Equal(timed.End, e.End); },
                    e => { Assert.Equal("All day", e.Title); Assert.Equal(allDay.Start, e.Start); Assert.Equal(allDay.End, e.End); });
                store.Close();
                byte[] original = File.ReadAllBytes(target);
                File.WriteAllText(json, broken);
                store.Prepare();
                Assert.Equal(original, File.ReadAllBytes(target));
                Assert.Equal(broken, File.ReadAllText(json));
            }
            finally
            {
                store.Close();
                cache.SetValue(null, previous);
                Directory.Delete(root, true);
            }
        }
    }
}
