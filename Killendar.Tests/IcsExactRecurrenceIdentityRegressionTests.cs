using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Microsoft.Data.Sqlite;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public class IcsExactRecurrenceIdentityRegressionTests
    {
        [Fact]
        public void ExactUntilPersistsAcrossUnchangedSeriesEditAndDateChangeClearsIt()
        {
            WithStore((store, path) =>
            {
                var parsed = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:persist-until\r\nDTSTART:20261101T073000Z\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;UNTIL=20261102T070000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
                store.ImportEvents(parsed.Events);
                store.Close();
                store.Open(path);
                var edit = Assert.Single(store.Events).Clone();
                Assert.Equal(new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc), edit.RepeatUntilExact);
                Assert.Equal(DateTimeKind.Utc, edit.RepeatUntilExact!.Value.Kind);
                edit.Title = "Updated";
                store.UpdateSeries(edit);
                Assert.Null(store.LoadError);
                Assert.Single(store.GetInRange(new DateTime(2026, 11, 1), new DateTime(2026, 11, 5)));
                edit = Assert.Single(store.Events).Clone();
                edit.RepeatUntil = new DateTime(2026, 11, 3);
                store.UpdateSeries(edit);
                Assert.Null(Assert.Single(store.Events).RepeatUntilExact);
            });
        }

        [Fact]
        public void AllDayUntilExportsDateAndSourceGapOverrideRetainsRawOriginalIdentityStandalone()
        {
            const string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:date-until\r\nDTSTART;VALUE=DATE:20260307\r\nDTEND;VALUE=DATE:20260308\r\nRRULE:FREQ=DAILY;UNTIL=20260309\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nUID:source-gap\r\nDTSTART:20260308T180000Z\r\nDURATION:PT1H\r\nRECURRENCE-ID;TZID=America/New_York:20260308T023000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var parsed = IcsService.ParseText(text);
            string output = IcsService.BuildIcs(parsed.Events);
            Assert.Contains("UNTIL=20260309\r\n", output);
            Assert.Contains("RECURRENCE-ID;TZID=\"America/New_York\":20260308T023000", output);
            Assert.Contains("BEGIN:VTIMEZONE", output);
            var again = IcsService.ParseText(output);
            Assert.Equal(3, Recurrence.Expand(Assert.Single(again.Events, e => e.IsSeries), new DateTime(2026, 3, 6), new DateTime(2026, 3, 11)).Count());
            var original = Assert.Single(parsed.Events, e => e.IsOverride);
            var reopened = Assert.Single(again.Events, e => e.IsOverride);
            Assert.Equal(original.OccurrenceAnchor, reopened.OccurrenceAnchor);
            Assert.Equal(original.OccurrenceBasis, reopened.OccurrenceBasis);
            Assert.Equal(original.OccurrenceZoneId, reopened.OccurrenceZoneId);
        }

        [Fact]
        public void SchemaFiveBackupReopensWithLegacyDateWideSkipsAndNoInventedStandaloneIdentity()
        {
            WithStore((store, path) =>
            {
                var master = new CalendarEvent { Title = "Legacy", Start = new DateTime(2026, 11, 1, 0, 30, 0),
                    End = new DateTime(2026, 11, 1, 1, 30, 0), Repeat = RepeatFreq.Daily, RepeatCount = 3,
                    SkipDates = [new DateTime(2026, 11, 2)] };
                store.Add(master);
                store.Close();
                SqliteConnection.ClearAllPools();
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "ALTER TABLE events DROP COLUMN skip_occurrences; ALTER TABLE events DROP COLUMN repeat_until_exact; ALTER TABLE events DROP COLUMN occurrence_anchor; ALTER TABLE events DROP COLUMN occurrence_basis; ALTER TABLE events DROP COLUMN occurrence_zone_id; UPDATE meta SET value='5' WHERE key='schema_version'";
                    cmd.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();
                byte[] before = File.ReadAllBytes(path);
                File.Copy(path, path + ".backup");
                for (int i = 0; i < 2; i++)
                {
                    store.Open(path);
                    Assert.Null(store.LoadError);
                    var row = Assert.Single(store.Events);
                    Assert.Empty(row.SkipOccurrences);
                    Assert.Null(row.RepeatUntilExact);
                    Assert.Null(row.OccurrenceAnchor);
                    Assert.Empty(store.GetOnDay(new DateTime(2026, 11, 2)));
                    Assert.Equal(2, store.GetInRange(new DateTime(2026, 11, 1), new DateTime(2026, 11, 5)).Count);
                    store.Close();
                }
                Assert.Equal(before, File.ReadAllBytes(path + ".backup"));
                var orphan = master.Clone();
                orphan.Repeat = RepeatFreq.None;
                orphan.SeriesId = master.Id;
                orphan.OccurrenceStart = master.Start;
                Assert.Throws<NotSupportedException>(() => IcsService.BuildIcs(new[] { orphan }));
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OriginalIdentitySurvivesSingleOccurrenceTypeConversionAndStandaloneExport(bool originalAllDay)
        {
            WithStore((store, path) =>
            {
                var master = new CalendarEvent { Title = "Master", AllDay = originalAllDay,
                    Start = new DateTime(2026, 1, 1, originalAllDay ? 0 : 9, 0, 0),
                    End = new DateTime(2026, 1, 1, originalAllDay ? 0 : 10, 0, 0).AddDays(originalAllDay ? 1 : 0),
                    Repeat = RepeatFreq.Daily, RepeatCount = 3 };
                store.Add(master);
                var edited = Assert.Single(store.GetOnDay(new DateTime(2026, 1, 2))).Clone();
                edited.AllDay = !originalAllDay;
                edited.Start = new DateTime(2026, 1, 2, edited.AllDay ? 0 : 14, 0, 0);
                edited.End = edited.Start.AddHours(edited.AllDay ? 24 : 1);
                store.SaveOccurrence(edited);
                Assert.Null(store.LoadError);
                store.Close();
                store.Open(path);
                var row = Assert.Single(store.Events, e => e.IsOverride);
                string output = IcsService.BuildIcs(new[] { row });
                Assert.Contains(originalAllDay ? "RECURRENCE-ID;VALUE=DATE:20260102" : "RECURRENCE-ID:20260102T090000\r\n", output);
                var parsed = Assert.Single(IcsService.ParseText(output).Events);
                Assert.Equal(row.OccurrenceBasis, parsed.OccurrenceBasis);
                Assert.Equal(row.OccurrenceAnchor, parsed.OccurrenceAnchor);
                Assert.Single(store.GetOnDay(new DateTime(2026, 1, 2)));
            });
        }

        [Fact]
        public void SameDisplayDateOverridesAndDeletionStayIndependentThroughRebaseRollbackAndReopen()
        {
            WithStore((store, path) =>
            {
                var parsed = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:two\r\nDTSTART:20261101T073000Z\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=3\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
                store.ImportEvents(parsed.Events);
                var rows = store.GetOnDay(new DateTime(2026, 11, 1));
                foreach (var row in rows)
                {
                    var edited = row.Clone();
                    edited.Title = row.Start.ToUniversalTime().ToString("O");
                    store.SaveOccurrence(edited);
                }
                Assert.Equal(2, store.Events.Count(e => e.IsOverride));
                store.DeleteOccurrence(rows[0]);
                var master = store.Events.Single(e => e.IsSeries).Clone();
                master.Start = master.Start.ToUniversalTime().AddHours(1).ToLocalTime();
                master.End = master.End.ToUniversalTime().AddHours(1).ToLocalTime();
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "CREATE TRIGGER reject_exact BEFORE UPDATE ON events WHEN OLD.series_id <> '' BEGIN SELECT RAISE(ABORT, 'exact fault'); END";
                    cmd.ExecuteNonQuery();
                    store.UpdateSeries(master);
                    Assert.NotNull(store.LoadError);
                    Assert.Equal(new DateTime(2026, 11, 1, 7, 30, 0), Assert.Single(store.Events, e => e.IsSeries).SkipOccurrences.Single());
                    cmd.CommandText = "DROP TRIGGER reject_exact";
                    cmd.ExecuteNonQuery();
                }
                store.UpdateSeries(master);
                Assert.Null(store.LoadError);
                store.Close();
                store.Open(path);
                var current = store.Events.Single(e => e.IsSeries);
                Assert.Equal(new DateTime(2026, 11, 1, 8, 30, 0), Assert.Single(current.SkipOccurrences));
                var replacement = Assert.Single(store.Events, e => e.IsOverride);
                Assert.Equal(new DateTime(2026, 11, 2, 8, 30, 0), replacement.OccurrenceAnchor);
                Assert.Equal(new DateTime(2026, 11, 2, 8, 30, 0, DateTimeKind.Utc), replacement.Start.ToUniversalTime());
            });
        }

        private static void WithStore(Action<EventStore, string> action)
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\exact-recurrence", Guid.NewGuid().ToString("N"));
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
        [InlineData("DTSTART:20261101T073000Z", "20261102T070000Z", "20261101T073000Z")]
        [InlineData("DTSTART:20261101T003000", "20261102T000000", "20261101T003000")]
        public void ExactUntilBeforeNextStartSurvivesPropertyOrderAndExport(string start, string until, string expected)
        {
            string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:until\r\nRRULE:FREQ=DAILY;UNTIL=" + until +
                "\r\n" + start + "\r\nDURATION:PT1H\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var parsed = IcsService.ParseText(text);
            Assert.Equal(0, parsed.Repeating);
            var master = Assert.Single(parsed.Events);
            Assert.Single(Recurrence.Expand(master, new DateTime(2026, 11, 1), new DateTime(2026, 11, 4)));
            string output = IcsService.BuildIcs(parsed.Events);
            Assert.Contains("UNTIL=" + until, output);
            Assert.Contains(expected, output);
            Assert.Single(Recurrence.Expand(Assert.Single(IcsService.ParseText(output).Events), new DateTime(2026, 11, 1), new DateTime(2026, 11, 4)));
        }

        [Theory]
        [InlineData("DTSTART:20261101T073000Z", "EXDATE:20261101T073000Z", "20261102T073000Z", 2)]
        [InlineData("DTSTART;TZID=America/New_York:20260307T023000", "EXDATE;TZID=America/New_York:20260308T023000", "20260309T023000", 3)]
        [InlineData("DTSTART;TZID=America/New_York:20260307T023000", "EXDATE;TZID=America/New_York:20260309T023000", "20260310T023000", 2)]
        public void ExactExclusionUsesOriginalCandidateAndCountBeforeExclusion(string start, string exclusion, string retained, int expectedCount)
        {
            string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:exclude\r\n" + exclusion + "\r\nRRULE:FREQ=DAILY;COUNT=3\r\n" + start +
                "\r\nDURATION:PT1H\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var parsed = IcsService.ParseText(text);
            var master = Assert.Single(parsed.Events);
            var from = start.Contains("202603") ? new DateTime(2026, 3, 6) : new DateTime(2026, 10, 31);
            Assert.Equal(expectedCount, Recurrence.Expand(master, from, from.AddDays(6)).Count());
            if (expectedCount == 3)
                Assert.Equal(new[] { "20260307T023000", "20260309T023000", "20260310T023000" },
                    Recurrence.Expand(master, from, from.AddDays(6)).Select(e => e.OccurrenceAnchor!.Value.ToString("yyyyMMdd'T'HHmmss")).ToArray());
            string output = IcsService.BuildIcs(parsed.Events);
            Assert.Contains(exclusion.Replace("TZID=America/New_York", "TZID=\"America/New_York\""), output);
            Assert.Equal(expectedCount, Recurrence.Expand(Assert.Single(IcsService.ParseText(output).Events), from, from.AddDays(6)).Count());
            Assert.Contains(Recurrence.Expand(master, from, from.AddDays(6)), occurrence =>
                occurrence.OccurrenceAnchor!.Value.ToString("yyyyMMdd'T'HHmmss") == retained.TrimEnd('Z'));
        }

        [Fact]
        public void DeletingOneUtcOccurrenceKeepsOtherStartOnSameDisplayDateAcrossReopenAndExport()
        {
            Assert.Equal("Pacific Standard Time", TimeZoneInfo.Local.Id);
            const string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:exact-occurrence-series\r\nDTSTART:20261101T073000Z\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=2\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\exact-recurrence", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                string path = Path.Combine(root, "calendar.kcal");
                store.Open(path);
                Assert.Equal(1, store.ImportEvents(IcsService.ParseText(text).Events));
                var before = store.GetOnDay(new DateTime(2026, 11, 1));
                Assert.Equal(2, before.Count);
                store.DeleteOccurrence(before[0]);
                Assert.Null(store.LoadError);
                Assert.Equal(new DateTime(2026, 11, 2, 7, 30, 0, DateTimeKind.Utc), Assert.Single(store.GetOnDay(new DateTime(2026, 11, 1))).Start.ToUniversalTime());
                store.Close();
                store.Open(path);
                var parsed = IcsService.ParseText(IcsService.BuildIcs(store.Events));
                Assert.Equal(new DateTime(2026, 11, 2, 7, 30, 0, DateTimeKind.Utc),
                    Assert.Single(Recurrence.Expand(Assert.Single(parsed.Events), new DateTime(2026, 11, 1), new DateTime(2026, 11, 3))).Start.ToUniversalTime());
            }
            finally
            {
                store.Close();
                Directory.Delete(root, true);
            }
        }
    }
}
