using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    // SS-001: a 1.4.0 row written under a Windows user-override time separator of '.' reads
    // like 10.30.00. The start and end columns already parse that shape; the skipped dates, the
    // exact repeat end and the reminder state must read it too.
    [Collection("Calendar settings")]
    public sealed class DottedStampRegressionTests
    {
        [Fact]
        public void DottedSkippedDatesAndExactRepeatEndStillLoad()
        {
            string directory = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string database = Path.Combine(directory, "dotted.kcal");
            var store = new EventStore();
            try
            {
                store.Open(database);
                store.Add(new CalendarEvent
                {
                    Title = "Series",
                    Start = new DateTime(2026, 3, 1, 10, 30, 0),
                    End = new DateTime(2026, 3, 1, 11, 30, 0),
                    Repeat = RepeatFreq.Daily,
                });
                store.Close();
                using (var db = new SqliteConnection("Data Source=" + database + ";Pooling=False"))
                {
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "UPDATE events SET skip_occurrences = '2026-03-03T10.30.00.0000000', " +
                                      "repeat_until_exact = '2026-03-10T09.30.00.0000000Z'";
                    cmd.ExecuteNonQuery();
                }
                store.Open(database);

                var loaded = store.Events.Single();
                Assert.Equal(new[] { new DateTime(2026, 3, 3, 10, 30, 0) }, loaded.SkipOccurrences);
                Assert.Equal(new DateTime(2026, 3, 10, 9, 30, 0, DateTimeKind.Utc), loaded.RepeatUntilExact);
            }
            finally
            {
                store.Close();
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void ReminderDismissedInADottedRowStaysDismissed()
        {
            var now = new DateTime(2026, 10, 21, 9, 0, 0);
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent
            {
                Title = "Dismissed",
                Start = now.AddMinutes(10),
                End = now.AddMinutes(70),
                ReminderMinutes = 15,
            });
            fixture.Store.DismissReminder(Assert.Single(fixture.Store.DueReminders(now)));
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "UPDATE reminder_state SET occurrence = replace(occurrence, ':', '.')";
                Assert.Equal(1, cmd.ExecuteNonQuery());
            }
            fixture.Store.Open(path);

            Assert.Empty(fixture.Store.DueReminders(now));
        }
    }
}
