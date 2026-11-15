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
    public sealed class ReviewRoundStoreRegressionTests
    {
        [Fact]
        public void ImportKeepsTheHighestSequenceCopyOfARepeatedUid()
        {
            using var fixture = new AppointmentFixture();
            var older = new CalendarEvent { Title = "Old time", Start = new DateTime(2026, 10, 20, 9, 0, 0),
                End = new DateTime(2026, 10, 20, 10, 0, 0), KeptProperties = ["SEQUENCE:1"] };
            var newer = older.Clone();
            newer.Title = "New time";
            newer.Start = new DateTime(2026, 10, 20, 14, 0, 0);
            newer.End = new DateTime(2026, 10, 20, 15, 0, 0);
            newer.KeptProperties = ["SEQUENCE:2"];

            var plan = fixture.Store.PlanImport(new[] { older, newer });

            var fresh = Assert.Single(plan.Fresh);
            Assert.Equal("New time", fresh.Title);
            Assert.Equal(1, plan.AlreadyPresent);
        }

        [Fact]
        public void UnreadableRowIdIsRepairedOnceSoEditsAndDeletesReachTheRow()
        {
            string root = Path.Combine(TestScratch.Root, "bad-row-id", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "calendar.kcal");
            var store = new EventStore();
            try
            {
                store.Open(path);
                store.Close();
                SqliteConnection.ClearAllPools();
                string start = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc).ToString("O");
                string end = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc).ToString("O");
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var command = db.CreateCommand();
                    command.CommandText = "INSERT INTO events(id,title,start_utc,end_utc,all_day,created_utc,modified_utc) " +
                        "VALUES ('abc','Hand made',$start,$end,0,$start,$start)";
                    command.Parameters.AddWithValue("$start", start);
                    command.Parameters.AddWithValue("$end", end);
                    command.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();

                store.Open(path);
                var row = Assert.Single(store.Events).Clone();
                row.Title = "Edited";
                store.Update(row);
                store.Close();
                store.Open(path);

                var reopened = Assert.Single(store.Events);
                Assert.Equal(row.Id, reopened.Id);
                Assert.Equal("Edited", reopened.Title);
                store.Delete(reopened.Id);
                store.Close();
                store.Open(path);
                Assert.Empty(store.Events);
            }
            finally
            {
                store.Close();
                SqliteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        }
    }
}
