using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class ReviewRoundMigrationRegressionTests
    {
        private static string DuplicateIds()
        {
            var first = new CalendarEvent { Title = "First", Start = new DateTime(2026, 1, 5, 9, 0, 0), End = new DateTime(2026, 1, 5, 10, 0, 0) };
            var second = first.Clone();
            second.Title = "Second";
            return JsonSerializer.Serialize(new[] { first, second }, new JsonSerializerOptions { IgnoreReadOnlyProperties = true });
        }

        [Theory]
        [InlineData("corrupt")]
        [InlineData("duplicates")]
        public void UnusableLegacyFileStillOpensACalendarAndIsLeftAlone(string kind)
        {
            Assert.False(File.Exists(Path.Combine(AppPaths.LocalData, "Killendar", "events.json")),
                "The suite profile must not hold a legacy source.");
            string root = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var cache = typeof(Settings).GetField("_cache", BindingFlags.Static | BindingFlags.NonPublic)!;
            object? previous = cache.GetValue(null);
            cache.SetValue(null, new Dictionary<string, string> { ["KillendarDataDir"] = root, ["ActiveKillendar"] = "Default.kcal" });
            var store = new EventStore();
            string json = Path.Combine(root, "events.json");
            string text = kind == "corrupt" ? "[{\"Title\": \"Broken\"," : DuplicateIds();
            try
            {
                File.WriteAllText(json, text);

                bool opened = new SecurityController(new SecurityHostFake(), store).Open(exitOnCancel: false);

                Assert.True(opened);
                Assert.True(store.IsOpen);
                Assert.Null(store.LoadError);
                if (kind == "corrupt")
                {
                    Assert.NotNull(store.MigrationWarning);
                    Assert.True(File.Exists(json));
                    Assert.Equal(text, File.ReadAllText(json));
                    Assert.Empty(store.Events);
                }
                else
                {
                    Assert.Equal(2, store.Events.Count);
                    Assert.NotEqual(store.Events[0].Id, store.Events[1].Id);
                }
            }
            finally
            {
                store.Close();
                SqliteConnection.ClearAllPools();
                cache.SetValue(null, previous);
                Directory.Delete(root, true);
            }
        }
    }
}
