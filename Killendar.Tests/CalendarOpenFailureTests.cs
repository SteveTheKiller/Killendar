using System;
using System.IO;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class CalendarOpenFailureTests
    {
        [Theory]
        [InlineData("schema")]
        [InlineData("rows")]
        [InlineData("categories")]
        public void FailedInitializationLeavesClosedEmptyStoreAndAllowsReopeningPreviousCalendar(string fault)
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string good = Path.Combine(root, "good.kcal");
            string bad = Path.Combine(root, "bad.kcal");
            var store = new EventStore();
            try
            {
                store.Open(bad);
                store.Add(new CalendarEvent { Title = "Bad", Start = new DateTime(2026, 1, 5), End = new DateTime(2026, 1, 6) });
                store.Close();
                using (var db = new SqliteConnection("Data Source=" + bad))
                {
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = fault == "schema" ? "DROP TABLE events; CREATE TABLE events (id TEXT)"
                        : fault == "rows" ? "ALTER TABLE events RENAME TO old_events; CREATE TABLE events AS SELECT * FROM old_events; UPDATE events SET title = NULL; DROP TABLE old_events"
                        : "DROP TABLE categories; CREATE TABLE categories (wrong TEXT)";
                    cmd.ExecuteNonQuery();
                }
                store.Open(good);
                store.Add(new CalendarEvent { Title = "Previous", Start = new DateTime(2026, 1, 5), End = new DateTime(2026, 1, 6) });

                Assert.NotNull(Record.Exception(() => store.Open(bad)));
                Assert.False(store.IsOpen);
                Assert.False(store.HasPassword);
                Assert.Equal("", store.FilePath);
                Assert.NotNull(store.LoadError);
                Assert.Empty(store.Events);
                using (new FileStream(bad, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                store.Open(good);
                Assert.Null(store.LoadError);
                Assert.Equal("Previous", Assert.Single(store.Events).Title);
            }
            finally { store.Close(); Directory.Delete(root, true); }
        }
    }
}
