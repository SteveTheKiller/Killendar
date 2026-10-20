using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class OccurrenceDeletionTests
    {
        [Fact]
        public void FailedOccurrenceDeletionPreservesItsEditsAndRetryPublishesOneChange()
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "calendar.kcal");
            var store = new EventStore();
            var start = new DateTime(2026, 1, 5, 9, 0, 0);
            try
            {
                store.Open(path);
                store.Add(new CalendarEvent { Title = "Series", Start = start, End = start.AddHours(1), Repeat = RepeatFreq.Daily });
                var edited = store.GetOnDay(start.AddDays(1)).Single().Clone();
                edited.Title = "Edited occurrence";
                edited.Start = edited.Start.AddHours(2);
                edited.End = edited.End.AddHours(2);
                store.SaveOccurrence(edited);
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "CREATE TRIGGER reject_master BEFORE UPDATE ON events WHEN OLD.series_id = '' BEGIN SELECT RAISE(ABORT, 'master fault'); END";
                    cmd.ExecuteNonQuery();
                }

                store.DeleteOccurrence(store.GetOnDay(start.AddDays(1)).Single());

                Assert.NotNull(store.LoadError);
                Assert.Equal("Edited occurrence", Assert.Single(store.GetOnDay(start.AddDays(1))).Title);
                Assert.Empty(store.Events.Single(e => !e.IsOverride).SkipDates);
                store.Close();
                store.Open(path);
                Assert.Equal("Edited occurrence", Assert.Single(store.GetOnDay(start.AddDays(1))).Title);
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "DROP TRIGGER reject_master";
                    cmd.ExecuteNonQuery();
                }
                int changes = 0;
                store.Changed += () => changes++;
                store.DeleteOccurrence(store.GetOnDay(start.AddDays(1)).Single());
                Assert.Null(store.LoadError);
                Assert.Empty(store.GetOnDay(start.AddDays(1)));
                Assert.Equal(1, changes);
                store.Close();
                store.Open(path);
                Assert.Empty(store.GetOnDay(start.AddDays(1)));
                Assert.Equal("Series", Assert.Single(store.GetOnDay(start)).Title);
            }
            finally
            {
                store.Close();
                Directory.Delete(root, true);
            }
        }
    }
}
