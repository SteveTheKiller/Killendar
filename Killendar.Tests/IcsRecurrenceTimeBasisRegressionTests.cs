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
    public class IcsRecurrenceTimeBasisRegressionTests
    {
        [Theory]
        [InlineData("Europe/London", false)]
        [InlineData("Europe/London", true)]
        [InlineData("America/New_York", false)]
        [InlineData("America/New_York", true)]
        [InlineData("America/Los_Angeles", false)]
        [InlineData("America/Los_Angeles", true)]
        public void SourceWallClockSurvivesUnequalDstDatesAndPropertyOrder(string zone, bool ruleFirst)
        {
            string dates = "DTSTART;TZID=" + zone + ":20261024T090000\r\nDTEND;TZID=" + zone + ":20261024T100000\r\n";
            const string rule = "RRULE:FREQ=DAILY;COUNT=10\r\n";
            string text = Wrap(ruleFirst ? rule + dates : dates + rule);
            WithStored(text, (store, master) =>
            {
                Assert.Equal(RecurrenceTimeBasis.SourceZone, master.RepeatTimeBasis);
                Assert.Equal(zone, master.RepeatZoneId);
                Assert.Equal(new DateTime(2026, 10, 24, 9, 0, 0), master.RepeatAnchor);
                var occurrences = store.GetInRange(new DateTime(2026, 10, 23), new DateTime(2026, 11, 5));
                Assert.Equal(10, occurrences.Count);
                for (int n = 0; n < 10; n++)
                {
                    int hour = zone == "Europe/London" ? (n == 0 ? 8 : 9) :
                        zone == "America/New_York" ? (n < 8 ? 13 : 14) : (n < 8 ? 16 : 17);
                    Assert.Equal(new DateTime(2026, 10, 24, hour, 0, 0, DateTimeKind.Utc).AddDays(n), occurrences[n].Start.ToUniversalTime());
                    Assert.Equal(TimeSpan.FromHours(1), occurrences[n].End.ToUniversalTime() - occurrences[n].Start.ToUniversalTime());
                    Assert.Equal(master.RepeatTimeBasis, occurrences[n].RepeatTimeBasis);
                    Assert.Equal(master.RepeatAnchor, occurrences[n].RepeatAnchor);
                    Assert.Equal(master.RepeatZoneId, occurrences[n].RepeatZoneId);
                    Assert.True(Recurrence.GeneratesDate(master, occurrences[n].Start));
                }
                var last = occurrences[9];
                Assert.Equal(last.Start.ToUniversalTime(), Assert.Single(store.GetInRange(last.Start.AddMinutes(-5), last.Start.AddMinutes(5))).Start.ToUniversalTime());
            });
        }

        [Fact]
        public void SourceGapKeepsOriginalWallAnchorForFollowingDates()
        {
            WithStored(Wrap("DTSTART;TZID=America/New_York:20260308T023000\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=2\r\n"), (store, master) =>
            {
                Assert.Equal(new DateTime(2026, 3, 8, 2, 30, 0), master.RepeatAnchor);
                Assert.Equal(new[] { new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), new DateTime(2026, 3, 9, 6, 30, 0, DateTimeKind.Utc) },
                    store.GetInRange(new DateTime(2026, 3, 7), new DateTime(2026, 3, 11)).Select(e => e.Start.ToUniversalTime()).ToArray());
            });
        }

        [Fact]
        public void EditingSourceGapOccurrenceUsesItsOriginalNominalAnchorAcrossRepeatedEdits()
        {
            WithStored(Wrap("DTSTART;TZID=America/New_York:20260308T023000\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=3\r\n"), (store, master) =>
            {
                var from = new DateTime(2026, 3, 7);
                var to = new DateTime(2026, 3, 12);
                var unchanged = store.GetInRange(from, to)[0].Clone();
                unchanged.Title = "Retitled";
                store.UpdateSeries(unchanged);
                Assert.Null(store.LoadError);
                Assert.Equal(new DateTime(2026, 3, 8, 2, 30, 0), Assert.Single(store.Events).RepeatAnchor);
                var edited = store.GetInRange(from, to)[0].Clone();
                edited.Start = edited.Start.AddHours(1);
                edited.End = edited.End.AddHours(1);
                store.UpdateSeries(edited);
                Assert.Null(store.LoadError);
                Assert.Equal(new DateTime(2026, 3, 8, 4, 30, 0), Assert.Single(store.Events).RepeatAnchor);
                store.Load();
                var afterFirst = store.GetInRange(from, to);
                Assert.Equal(3, afterFirst.Count);
                for (int n = 0; n < 3; n++)
                    Assert.Equal(new DateTime(2026, 3, 8, 8, 30, 0, DateTimeKind.Utc).AddDays(n), afterFirst[n].Start.ToUniversalTime());
                var second = afterFirst[1].Clone();
                second.Start = second.Start.AddHours(1);
                second.End = second.End.AddHours(1);
                store.UpdateSeries(second);
                Assert.Null(store.LoadError);
                store.Load();
                Assert.Equal(new DateTime(2026, 3, 8, 5, 30, 0), Assert.Single(store.Events).RepeatAnchor);
                var afterSecond = store.GetInRange(from, to);
                Assert.Equal(3, afterSecond.Count);
                for (int n = 0; n < 3; n++)
                    Assert.Equal(new DateTime(2026, 3, 8, 9, 30, 0, DateTimeKind.Utc).AddDays(n), afterSecond[n].Start.ToUniversalTime());
            });
        }

        [Fact]
        public void PhysicalDurationIsPreservedAcrossSourceClockChange()
        {
            WithStored(Wrap("DTSTART;TZID=America/New_York:20261101T003000\r\nDTEND;TZID=America/New_York:20261101T023000\r\nRRULE:FREQ=DAILY;COUNT=2\r\n"), (store, master) =>
            {
                var occurrences = store.GetInRange(new DateTime(2026, 10, 31), new DateTime(2026, 11, 4));
                Assert.Equal(2, occurrences.Count);
                Assert.All(occurrences, e => Assert.Equal(TimeSpan.FromHours(3), e.End.ToUniversalTime() - e.Start.ToUniversalTime()));
            });
        }

        [Fact]
        public void SourceCandidateLookupOutsideCountReportsFailureWithoutChangingSeries()
        {
            WithStored(Wrap("DTSTART:20261025T160000Z\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=2\r\n"), (store, master) =>
            {
                var before = store.GetInRange(new DateTime(2026, 10, 24), new DateTime(2026, 11, 5));
                var edited = before[0].Clone();
                edited.OccurrenceStart = new DateTime(2026, 10, 29, 16, 0, 0, DateTimeKind.Utc).ToLocalTime();
                edited.Start = edited.OccurrenceStart.Value.AddHours(1);
                edited.End = edited.Start.AddHours(1);
                store.UpdateSeries(edited);
                Assert.NotNull(store.LoadError);
                Assert.Contains("original recurrence occurrence", store.LoadError!);
                Assert.Equal(master.RepeatAnchor, Assert.Single(store.Events).RepeatAnchor);
                Assert.Equal(before.Select(e => e.Start).ToArray(), store.GetInRange(new DateTime(2026, 10, 24), new DateTime(2026, 11, 5)).Select(e => e.Start).ToArray());
            });
        }

        [Fact]
        public void EarliestSourceCandidateLookupClampsBeforeMinimumDate()
        {
            WithStored(Wrap("DTSTART;TZID=UTC:00010101T120000\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=2\r\n"), (store, master) =>
            {
                var edited = store.GetInRange(DateTime.MinValue, new DateTime(1, 1, 4))[0].Clone();
                edited.Start = edited.Start.AddHours(1);
                edited.End = edited.End.AddHours(1);
                store.UpdateSeries(edited);
                Assert.Null(store.LoadError);
                Assert.Equal(new DateTime(1, 1, 1, 13, 0, 0), Assert.Single(store.Events).RepeatAnchor);
                Assert.Equal(new[] { new DateTime(1, 1, 1, 13, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 2, 13, 0, 0, DateTimeKind.Utc) },
                    store.GetInRange(DateTime.MinValue, new DateTime(1, 1, 4)).Select(e => e.Start.ToUniversalTime()).ToArray());
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LocalAndFloatingAllDaySeriesKeepTheirExistingBasis(bool allDay)
        {
            string dates = allDay ? "DTSTART;VALUE=DATE:20261031\r\nDTEND;VALUE=DATE:20261101\r\n" :
                "DTSTART:20261031T090000\r\nDTEND:20261031T100000\r\n";
            WithStored(Wrap(dates + "RRULE:FREQ=DAILY;COUNT=3\r\n"), (store, master) =>
            {
                Assert.Equal(RecurrenceTimeBasis.Local, master.RepeatTimeBasis);
                Assert.Null(master.RepeatAnchor);
                var occurrences = store.GetInRange(new DateTime(2026, 10, 30), new DateTime(2026, 11, 4));
                Assert.Equal(3, occurrences.Count);
                for (int n = 0; n < 3; n++)
                    Assert.Equal(new DateTime(2026, 10, 31, allDay ? 0 : 9, 0, 0).AddDays(n), occurrences[n].Start);
            });
        }

        [Fact]
        public void CountSkipsAndMovedOverridesRemainVisibleAfterReopen()
        {
            WithStored(Wrap("DTSTART:20261025T160000Z\r\nDTEND:20261025T170000Z\r\nRRULE:FREQ=DAILY;COUNT=10\r\n"), (store, master) =>
            {
                var from = new DateTime(2026, 10, 24);
                var to = new DateTime(2026, 11, 5);
                var occurrences = store.GetInRange(from, to);
                store.DeleteOccurrence(occurrences[2]);
                var edited = occurrences[8].Clone();
                edited.Title = "Moved";
                edited.Start = edited.Start.AddHours(2);
                edited.End = edited.End.AddHours(2);
                store.SaveOccurrence(edited);
                Assert.Null(store.LoadError);
                store.Load();
                var result = store.GetInRange(from, to);
                Assert.Equal(9, result.Count);
                Assert.Equal(new DateTime(2026, 11, 2, 18, 0, 0, DateTimeKind.Utc), Assert.Single(result, e => e.Title == "Moved").Start.ToUniversalTime());
                Assert.DoesNotContain(result, e => e.Start.ToUniversalTime() == new DateTime(2026, 10, 27, 16, 0, 0, DateTimeKind.Utc));
            });
        }

        [Fact]
        public void TwoUtcStartsCanShareOneDisplayDateRequiringExactIdentityFollowup()
        {
            Assert.Equal("Pacific Standard Time", TimeZoneInfo.Local.Id);
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART:20261101T073000Z\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=2\r\n")).Events);
            var occurrences = Recurrence.Expand(master, new DateTime(2026, 11, 1), new DateTime(2026, 11, 2)).ToArray();
            Assert.Equal(2, occurrences.Length);
            Assert.Equal(occurrences[0].Start.Date, occurrences[1].Start.Date);
            Assert.NotEqual(occurrences[0].Start.ToUniversalTime(), occurrences[1].Start.ToUniversalTime());
        }

        private static string Wrap(string properties) => "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:basis-regression-series\r\n" + properties + "END:VEVENT\r\nEND:VCALENDAR\r\n";

        private static void WithStored(string text, Action<EventStore, CalendarEvent> verify)
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\recurrence-basis", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                string path = Path.Combine(root, "calendar.kcal");
                store.Open(path);
                Assert.Equal(1, store.ImportEvents(IcsService.ParseText(text).Events));
                store.Close();
                store.Open(path);
                Assert.Null(store.LoadError);
                verify(store, Assert.Single(store.Events));
            }
            finally
            {
                store.Close();
                Directory.Delete(root, true);
            }
        }
        [Fact]
        public void UtcDailySeriesKeepsItsUtcHourAfterStoredReopenAcrossDst()
        {
            const string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:utc-basis-series\r\nRRULE:FREQ=DAILY;COUNT=10\r\nDTSTART:20261025T160000Z\r\nDTEND:20261025T170000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\recurrence-basis", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                string path = Path.Combine(root, "calendar.kcal");
                store.Open(path);
                Assert.Equal(1, store.ImportEvents(IcsService.ParseText(text).Events));
                store.Close();
                store.Open(path);
                Assert.Null(store.LoadError);
                var master = Assert.Single(store.Events);
                var from = new DateTime(2026, 10, 24);
                var to = new DateTime(2026, 11, 5);
                var expected = Enumerable.Range(0, 10).Select(n => new DateTime(2026, 10, 25, 16, 0, 0, DateTimeKind.Utc).AddDays(n)).ToArray();
                Assert.Equal(expected, Recurrence.Expand(master, from, to).Select(e => e.Start.ToUniversalTime()).ToArray());
                Assert.Equal(expected, store.GetInRange(from, to).Select(e => e.Start.ToUniversalTime()).ToArray());
            }
            finally
            {
                store.Close();
                Directory.Delete(root, true);
            }
        }

        [Theory]
        [InlineData("DURATION:PT2H\r\n", 2)]
        [InlineData("DTEND:20261101T093000Z\r\n", 1)]
        [InlineData("", 1)]
        public void ImportedDurationUsesElapsedTimeAcrossDisplayFallBack(string endProperty, int hours)
        {
            WithStored(Wrap("DTSTART:20261101T083000Z\r\n" + endProperty + "RRULE:FREQ=DAILY;COUNT=2\r\n"), (store, master) =>
            {
                Assert.Equal(TimeSpan.FromHours(hours), master.End.ToUniversalTime() - master.Start.ToUniversalTime());
                var occurrences = store.GetInRange(new DateTime(2026, 11, 1), new DateTime(2026, 11, 4));
                Assert.Equal(2, occurrences.Count);
                Assert.All(occurrences, e => Assert.Equal(TimeSpan.FromHours(hours), e.End.ToUniversalTime() - e.Start.ToUniversalTime()));
            });
        }

        [Fact]
        public void SourceAnchorEditSharesTransactionFailureAndRetryBoundary()
        {
            WithStored(Wrap("DTSTART;TZID=Europe/London:20261024T090000\r\nDTEND;TZID=Europe/London:20261024T100000\r\nRRULE:FREQ=DAILY;COUNT=10\r\n"), (store, master) =>
            {
                var from = new DateTime(2026, 10, 23);
                var to = new DateTime(2026, 11, 5);
                var before = store.GetInRange(from, to).Select(e => e.Start.ToUniversalTime()).ToArray();
                var edited = store.GetInRange(from, to)[8].Clone();
                edited.Start = edited.Start.AddHours(1);
                edited.End = edited.End.AddHours(1);
                using (var db = new SqliteConnection("Data Source=" + store.FilePath))
                {
                    db.Open();
                    using (var command = db.CreateCommand())
                    {
                        command.CommandText = "CREATE TRIGGER reject_basis BEFORE UPDATE ON events BEGIN SELECT RAISE(ABORT, 'basis fault'); END";
                        command.ExecuteNonQuery();
                    }
                    store.UpdateSeries(edited);
                    Assert.NotNull(store.LoadError);
                    Assert.Equal(before, store.GetInRange(from, to).Select(e => e.Start.ToUniversalTime()).ToArray());
                    Assert.Equal(master.RepeatAnchor, Assert.Single(store.Events).RepeatAnchor);
                    using (var command = db.CreateCommand())
                    {
                        command.CommandText = "DROP TRIGGER reject_basis";
                        command.ExecuteNonQuery();
                    }
                }
                store.UpdateSeries(edited);
                Assert.Null(store.LoadError);
                Assert.Equal(new DateTime(2026, 10, 24, 10, 0, 0), Assert.Single(store.Events).RepeatAnchor);
                store.Load();
                Assert.Equal(before.Select(d => d.AddHours(1)).ToArray(), store.GetInRange(from, to).Select(e => e.Start.ToUniversalTime()).ToArray());
            });
        }

        [Fact]
        public void OldSchemaBackupAndReopenPreserveNativeRowsWithLocalDefaults()
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\recurrence-basis", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                string path = Path.Combine(root, "old.kcal");
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var command = db.CreateCommand();
                    command.CommandText = "CREATE TABLE events (id TEXT PRIMARY KEY, title TEXT NOT NULL, start_utc TEXT NOT NULL, end_utc TEXT NOT NULL, all_day INTEGER NOT NULL DEFAULT 0, location TEXT NOT NULL DEFAULT '', description TEXT NOT NULL DEFAULT '', attendees TEXT NOT NULL DEFAULT '', created_utc TEXT NOT NULL, modified_utc TEXT NOT NULL, repeat_freq INTEGER NOT NULL DEFAULT 0, repeat_count INTEGER NOT NULL DEFAULT 0); CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL); INSERT INTO meta VALUES ('schema_version','3'); INSERT INTO events(id,title,start_utc,end_utc,all_day,created_utc,modified_utc,repeat_freq,repeat_count) VALUES ('11111111-1111-1111-1111-111111111111','Native',$start,$end,0,$start,$start,1,3), ('22222222-2222-2222-2222-222222222222','Floating','2026-10-31T00:00:00','2026-11-01T00:00:00',1,$start,$start,1,3);";
                    command.Parameters.AddWithValue("$start", new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Local).ToUniversalTime().ToString("O"));
                    command.Parameters.AddWithValue("$end", new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Local).ToUniversalTime().ToString("O"));
                    command.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();
                byte[] original = File.ReadAllBytes(path);
                string backup = path + ".backup";
                File.Copy(path, backup);
                for (int reopen = 0; reopen < 2; reopen++)
                {
                    store.Open(path);
                    Assert.Null(store.LoadError);
                    Assert.Equal(2, store.Events.Count);
                    Assert.All(store.Events, e => { Assert.Equal(RecurrenceTimeBasis.Local, e.RepeatTimeBasis); Assert.Null(e.RepeatAnchor); Assert.Equal("", e.RepeatZoneId); });
                    var rows = store.GetInRange(new DateTime(2026, 10, 30), new DateTime(2026, 11, 4));
                    Assert.Equal(6, rows.Count);
                    Assert.All(rows.Where(e => e.Title == "Native"), e => Assert.Equal(9, e.Start.Hour));
                    Assert.All(rows.Where(e => e.Title == "Floating"), e => Assert.Equal(0, e.Start.Hour));
                    store.Close();
                }
                Assert.Equal(original, File.ReadAllBytes(backup));
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var command = db.CreateCommand();
                    command.CommandText = "SELECT value FROM meta WHERE key='schema_version'";
                    Assert.Equal("4", command.ExecuteScalar());
                }
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
