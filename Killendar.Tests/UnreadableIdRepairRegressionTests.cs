using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    // KD150-R1: a damaged or hand-edited file can hold a NULL or empty id. The repair
    // must give each such row its own readable id, and must not turn the empty series_id that
    // every standalone appointment carries into a link to the repaired row.
    [Collection("Calendar settings")]
    public sealed class UnreadableIdRepairRegressionTests
    {
        [Theory]
        [InlineData("NULL, NULL")]
        [InlineData("'', 'x'")]
        public void DamagedIdsGetTheirOwnIdsAndStandaloneRowsStayStandalone(string ids)
        {
            string directory = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string database = Path.Combine(directory, "damaged.kcal");
            var store = new EventStore();
            try
            {
                store.Open(database);
                for (int i = 0; i < 3; i++)
                    store.Add(new CalendarEvent
                    {
                        Title = "Row " + i,
                        Start = new DateTime(2026, 3, 1 + i, 10, 0, 0),
                        End = new DateTime(2026, 3, 1 + i, 11, 0, 0),
                    });
                store.Close();
                string[] values = ids.Split(',').Select(v => v.Trim()).ToArray();
                using (var db = new SqliteConnection("Data Source=" + database + ";Pooling=False"))
                {
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText =
                        "UPDATE events SET id = " + values[0] + " WHERE title = 'Row 0'; " +
                        "UPDATE events SET id = " + values[1] + " WHERE title = 'Row 1';";
                    cmd.ExecuteNonQuery();
                }

                store.Open(database);

                var loaded = store.Events.OrderBy(e => e.Title).ToList();
                Assert.Equal(3, loaded.Count);
                Assert.Equal(3, loaded.Select(e => e.Id).Distinct().Count());
                Assert.All(loaded, e => Assert.Null(e.SeriesId));
            }
            finally
            {
                store.Close();
                Directory.Delete(directory, true);
            }
        }
    }
}
