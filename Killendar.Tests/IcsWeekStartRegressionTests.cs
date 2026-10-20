using System;
using System.Linq;
using System.IO;
using Killendar.Models;
using Microsoft.Data.Sqlite;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public class IcsWeekStartRegressionTests
    {
        [Fact]
        public void MondayWeekStartKeepsSundayInTheSameBiweeklyWeek()
        {
            const string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:week-start-series\r\nDTSTART;VALUE=DATE:20260105\r\nDTEND;VALUE=DATE:20260106\r\nRRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,SU;WKST=MO;COUNT=4\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var master = Assert.Single(IcsService.ParseText(text).Events);
            Assert.Equal(new[] { new DateTime(2026, 1, 5), new DateTime(2026, 1, 11), new DateTime(2026, 1, 19), new DateTime(2026, 1, 25) },
                Recurrence.Expand(master, new DateTime(2026, 1, 1), new DateTime(2026, 2, 8)).Select(e => e.Start).ToArray());
        }

        [Theory]
        [InlineData(";WKST=MO", false)]
        [InlineData(";WKST=SU", true)]
        [InlineData("", false)]
        public void WeekBoundarySurvivesStorageAndExportRoundTrip(string weekStart, bool sunday)
        {
            string text = Calendar("20260105", "MO,SU", weekStart);
            var expected = sunday
                ? new[] { new DateTime(2026, 1, 5), new DateTime(2026, 1, 18), new DateTime(2026, 1, 19), new DateTime(2026, 2, 1) }
                : new[] { new DateTime(2026, 1, 5), new DateTime(2026, 1, 11), new DateTime(2026, 1, 19), new DateTime(2026, 1, 25) };
            WithStore((store, path) =>
            {
                store.Open(path);
                Assert.Equal(1, store.ImportEvents(IcsService.ParseText(text).Events));
                store.Close();
                store.Open(path);
                var master = Assert.Single(store.Events);
                Assert.Equal(sunday ? DayOfWeek.Sunday : DayOfWeek.Monday, master.RepeatWeekStart);
                Assert.Equal(expected, Dates(master));
                string exported = IcsService.BuildIcs(store.Events);
                Assert.Contains(sunday ? ";WKST=SU" : ";WKST=MO", exported);
                Assert.Equal(expected, Dates(Assert.Single(IcsService.ParseText(exported).Events)));
            });
        }

        [Fact]
        public void PartialFirstWeekKeepsOrdinalsAccurateAfterRangeJump()
        {
            var master = Assert.Single(IcsService.ParseText(Calendar("20260107", "MO,WE,SU", ";WKST=MO")).Events);
            Assert.Equal(new[] { new DateTime(2026, 1, 7), new DateTime(2026, 1, 11), new DateTime(2026, 1, 19), new DateTime(2026, 1, 21) }, Dates(master));
            Assert.Equal(new[] { new DateTime(2026, 1, 19), new DateTime(2026, 1, 21) },
                Recurrence.Expand(master, new DateTime(2026, 1, 18), new DateTime(2026, 2, 8)).Select(e => e.Start).ToArray());
            Assert.False(Recurrence.GeneratesDate(master, new DateTime(2026, 1, 25)));
        }

        [Theory]
        [InlineData("NOPE")]
        [InlineData("8")]
        [InlineData("")]
        public void InvalidWeekStartReportsUnsupportedRecurrence(string invalid)
        {
            var parsed = IcsService.ParseText(Calendar("20260105", "MO,SU", ";WKST=" + invalid));
            Assert.Equal(1, parsed.Repeating);
            Assert.Equal(RepeatFreq.None, Assert.Single(parsed.Events).Repeat);
        }

        [Fact]
        public void OldSchemaMigrationPreservesNativeSundayWeekAndBackup()
        {
            WithStore((store, path) =>
            {
                store.Open(path);
                store.Add(new CalendarEvent { Title = "Native", AllDay = true, Start = new DateTime(2026, 1, 5), End = new DateTime(2026, 1, 6),
                    Repeat = RepeatFreq.Weekly, RepeatInterval = 2, RepeatCount = 4, RepeatDays = { DayOfWeek.Monday, DayOfWeek.Sunday } });
                store.Close();
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var command = db.CreateCommand();
                    command.CommandText = "ALTER TABLE events DROP COLUMN repeat_week_start; UPDATE meta SET value='4' WHERE key='schema_version';";
                    command.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();
                byte[] before = File.ReadAllBytes(path);
                string backup = path + ".backup";
                File.Copy(path, backup);
                for (int reopen = 0; reopen < 2; reopen++)
                {
                    store.Open(path);
                    Assert.Null(store.LoadError);
                    var master = Assert.Single(store.Events);
                    Assert.Equal(DayOfWeek.Sunday, master.RepeatWeekStart);
                    Assert.Equal(new[] { new DateTime(2026, 1, 5), new DateTime(2026, 1, 18), new DateTime(2026, 1, 19), new DateTime(2026, 2, 1) }, Dates(master));
                    var edited = master.Clone();
                    edited.Title = "Native renamed";
                    store.UpdateSeries(edited);
                    Assert.Null(store.LoadError);
                    Assert.Equal(DayOfWeek.Sunday, Assert.Single(store.Events).RepeatWeekStart);
                    store.Close();
                }
                Assert.Equal(before, File.ReadAllBytes(backup));
            });
        }

        private static string Calendar(string start, string days, string weekStart) => "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:week-start-series\r\nDTSTART;VALUE=DATE:" + start +
            "\r\nDURATION:P1D\r\nRRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=" + days + weekStart + ";COUNT=4\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        private static DateTime[] Dates(CalendarEvent master) => Recurrence.Expand(master, new DateTime(2026, 1, 1), new DateTime(2026, 2, 8)).Select(e => e.Start).ToArray();

        private static void WithStore(Action<EventStore, string> action)
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\week-start", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try { action(store, Path.Combine(root, "calendar.kcal")); }
            finally { store.Close(); SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
        }
    }
}
