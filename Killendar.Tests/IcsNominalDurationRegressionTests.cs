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
    public class IcsNominalDurationRegressionTests
    {
        [Fact]
        public void ElapsedTwentyFourHoursRemainSeparateFromNominalDayAcrossSpringChange()
        {
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART;TZID=America/New_York:20260307T003000\r\nDURATION:P1DT24H\r\nRRULE:FREQ=DAILY;COUNT=2\r\n")).Events);
            Assert.Equal(1, master.DurationDays);
            Assert.Equal(TimeSpan.FromHours(24).Ticks, master.DurationTimeTicks);

            var imported = Assert.Single(IcsService.ParseText(IcsService.BuildIcs(new[] { master })).Events);
            Assert.Equal(1, imported.DurationDays);
            Assert.Equal(TimeSpan.FromHours(24).Ticks, imported.DurationTimeTicks);
            var occurrences = Recurrence.Expand(imported, new DateTime(2026, 3, 6), new DateTime(2026, 3, 11)).ToArray();

            Assert.Equal(2, occurrences.Length);
            Assert.Equal(new DateTime(2026, 3, 9, 5, 30, 0, DateTimeKind.Utc), occurrences[0].End.ToUniversalTime());
            Assert.Equal(TimeSpan.FromHours(48), occurrences[0].End.ToUniversalTime() - occurrences[0].Start.ToUniversalTime());
            Assert.Equal(new DateTime(2026, 3, 10, 4, 30, 0, DateTimeKind.Utc), occurrences[1].End.ToUniversalTime());
            Assert.Equal(TimeSpan.FromHours(47), occurrences[1].End.ToUniversalTime() - occurrences[1].Start.ToUniversalTime());
        }

        [Fact]
        public void SchemaSixReadOnlyProjectionAndWritableMigrationKeepLegacyDurationDefaults()
        {
            WithStore((store, path) =>
            {
                store.Add(new CalendarEvent { Title = "Legacy", Start = new DateTime(2026, 11, 1, 9, 0, 0),
                    End = new DateTime(2026, 11, 1, 10, 0, 0), Repeat = RepeatFreq.Daily, RepeatCount = 2 });
                store.Close();
                SqliteConnection.ClearAllPools();
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var command = db.CreateCommand();
                    command.CommandText = "ALTER TABLE events DROP COLUMN duration_days; ALTER TABLE events DROP COLUMN duration_time_ticks; UPDATE meta SET value='6' WHERE key='schema_version'";
                    command.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();
                byte[] original = File.ReadAllBytes(path);
                File.Copy(path, path + ".backup");
                store.OpenReadOnly(path);
                Assert.Null(store.LoadError);
                Assert.Equal(0, Assert.Single(store.Events).DurationDays);
                Assert.Equal(0, Assert.Single(store.Events).DurationTimeTicks);
                Assert.Equal(2, store.GetInRange(new DateTime(2026, 11, 1), new DateTime(2026, 11, 4)).Count);
                store.Close();
                SqliteConnection.ClearAllPools();
                Assert.Equal(original, File.ReadAllBytes(path));
                for (int reopen = 0; reopen < 2; reopen++)
                {
                    store.Open(path);
                    Assert.Null(store.LoadError);
                    Assert.Equal(0, Assert.Single(store.Events).DurationDays);
                    Assert.Equal(0, Assert.Single(store.Events).DurationTimeTicks);
                    store.Close();
                }
                Assert.Equal(original, File.ReadAllBytes(path + ".backup"));
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var command = db.CreateCommand();
                    command.CommandText = "SELECT value FROM meta WHERE key='schema_version'";
                    Assert.Equal("7", command.ExecuteScalar());
                }
            });
        }

        [Theory]
        [InlineData("P1D", 25, true)]
        [InlineData("P1DT2H", 27, true)]
        [InlineData("PT24H", 24, true)]
        [InlineData("P1D", 25, false)]
        public void NominalAndElapsedDurationsSurviveStorageAndExport(string duration, int hours, bool series)
        {
            WithStore((store, path) =>
            {
                store.ImportEvents(IcsService.ParseText(Wrap("DTSTART;TZID=America/New_York:20261101T003000\r\nDURATION:" + duration + "\r\n" +
                    (series ? "RRULE:FREQ=DAILY;COUNT=2\r\n" : ""))).Events);
                store.Close();
                store.Open(path);
                var row = Assert.Single(store.Events);
                Assert.Equal(duration == "PT24H" ? 0 : 1, row.DurationDays);
                Assert.Equal(duration == "P1DT2H" ? TimeSpan.FromHours(2).Ticks : 0, row.DurationTimeTicks);
                Assert.Equal(TimeSpan.FromHours(hours), row.End.ToUniversalTime() - row.Start.ToUniversalTime());
                string output = IcsService.BuildIcs(store.Events);
                Assert.Contains("BEGIN:VTIMEZONE", output);
                Assert.Contains("DURATION:" + (duration == "PT24H" ? "PT24H0S" : duration == "P1DT2H" ? "P1DT2H0S" : "P1DT0H0S"), output);
                var imported = Assert.Single(IcsService.ParseText(output).Events);
                Assert.Equal(row.DurationDays, imported.DurationDays);
                Assert.Equal(row.DurationTimeTicks, imported.DurationTimeTicks);
                Assert.Equal(row.End.ToUniversalTime(), imported.End.ToUniversalTime());
                if (series)
                {
                    var rows = Recurrence.Expand(imported, new DateTime(2026, 10, 31), new DateTime(2026, 11, 5)).ToArray();
                    Assert.Equal(2, rows.Length);
                    Assert.Equal(TimeSpan.FromHours(duration == "P1DT2H" ? 26 : 24), rows[1].End.ToUniversalTime() - rows[1].Start.ToUniversalTime());
                }
            });
        }

        [Fact]
        public void AllDayDurationKeepsFloatingDateRepresentation()
        {
            var row = Assert.Single(IcsService.ParseText(Wrap("DTSTART;VALUE=DATE:20261101\r\nDURATION:P1D\r\nRRULE:FREQ=DAILY;COUNT=2\r\n")).Events);
            Assert.Equal(0, row.DurationDays);
            Assert.Equal(0, row.DurationTimeTicks);
            Assert.Equal(new DateTime(2026, 11, 2), row.End);
        }

        [Theory]
        [InlineData("P2147483647D")]
        [InlineData("P1DT2147483647H")]
        public void OverflowingDurationDoesNotDiscardFollowingValidEvent(string duration)
        {
            string text = Wrap("DTSTART;TZID=America/New_York:20261101T003000\r\nDURATION:" + duration + "\r\n");
            text = text.Replace("END:VCALENDAR", "BEGIN:VEVENT\r\nUID:valid\r\nDTSTART:20261101T180000Z\r\nDURATION:PT1H\r\nSUMMARY:Valid\r\nEND:VEVENT\r\nEND:VCALENDAR");
            var parsed = IcsService.ParseText(text);
            Assert.Equal(1, parsed.Unreadable);
            Assert.Equal("Valid", Assert.Single(parsed.Events).Title);
        }

        [Fact]
        public void PersistedOverflowingDurationIsFilteredWithoutQueryCrash()
        {
            WithStore((store, path) =>
            {
                store.ImportEvents(IcsService.ParseText(Wrap("DTSTART;TZID=America/New_York:20261101T003000\r\nDURATION:P1D\r\nRRULE:FREQ=DAILY;COUNT=2\r\n")).Events);
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var command = db.CreateCommand();
                    command.CommandText = "UPDATE events SET duration_days=2147483647,duration_time_ticks=9223372036854775807";
                    command.ExecuteNonQuery();
                }
                store.Load();
                Assert.Null(store.LoadError);
                Assert.Empty(store.GetInRange(new DateTime(2026, 11, 1), new DateTime(2026, 11, 5)));
            });
        }

        private static string Wrap(string properties) => "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:nominal-duration\r\n" + properties + "END:VEVENT\r\nEND:VCALENDAR\r\n";

        private static void WithStore(Action<EventStore, string> action)
        {
            string root = Path.Combine(TestScratch.Root + @"\nominal-duration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                string path = Path.Combine(root, "calendar.kcal");
                store.Open(path);
                action(store, path);
            }
            finally
            {
                store.Close();
                SqliteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        }

        [Theory]
        [InlineData("America/New_York", "20261101T003000", "P1D", "2026-11-02T05:30:00Z", 25)]
        [InlineData("America/New_York", "20260308T003000", "P1D", "2026-03-09T04:30:00Z", 23)]
        [InlineData("America/New_York", "20261101T003000", "P1DT2H", "2026-11-02T07:30:00Z", 27)]
        public void NominalDaysAreAppliedInSourceZoneBeforeElapsedSubdayTime(string zone, string start, string duration, string expectedEnd, int hours)
        {
            string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:nominal-duration\r\nDTSTART;TZID=" + zone + ":" + start +
                "\r\nDURATION:" + duration + "\r\nRRULE:FREQ=DAILY;COUNT=2\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var master = Assert.Single(IcsService.ParseText(text).Events);
            var from = new DateTime(master.Start.Year, master.Start.Month, 1);
            var first = Recurrence.Expand(master, from.AddDays(-1), from.AddMonths(1)).OrderBy(e => e.Start.ToUniversalTime()).First();

            Assert.Equal(DateTime.Parse(expectedEnd, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), first.End.ToUniversalTime());
            Assert.Equal(TimeSpan.FromHours(hours), first.End.ToUniversalTime() - first.Start.ToUniversalTime());
        }
    }
}
