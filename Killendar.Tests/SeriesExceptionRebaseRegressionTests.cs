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
    public sealed class SeriesExceptionRebaseRegressionTests
    {
        [Fact]
        public void MovingMondaySeriesToTuesdayMovesItsEditedOccurrence()
        {
            using var fixture = new CalendarFixture();
            var store = fixture.Store;
            var master = AddWeeklySeries(store);
            var exception = store.GetOnDay(new DateTime(2026, 1, 12)).Single().Clone();
            exception.Title = "Personal edit";
            exception.Start = exception.Start.AddHours(2);
            exception.End = exception.End.AddHours(3);
            store.SaveOccurrence(exception);
            var edited = master.Clone();
            edited.Start = edited.Start.AddDays(1);
            edited.End = edited.End.AddDays(1);
            edited.RepeatDays = [DayOfWeek.Tuesday];
            int changes = 0;
            store.Changed += () => changes++;

            store.UpdateSeries(edited);

            Assert.Null(store.LoadError);
            Assert.Equal(1, changes);
            Assert.Empty(store.GetOnDay(new DateTime(2026, 1, 12)));
            AssertMovedException(store);
            fixture.Reopen();
            Assert.Empty(store.GetOnDay(new DateTime(2026, 1, 12)));
            AssertMovedException(store);
        }

        [Theory]
        [InlineData("count")]
        [InlineData("interval")]
        [InlineData("weekday")]
        [InlineData("until")]
        public void ChangedPatternRemovesExceptionsItNoLongerGenerates(string change)
        {
            using var fixture = new CalendarFixture();
            var store = fixture.Store;
            AddWeeklySeries(store);
            var exception = store.GetOnDay(new DateTime(2026, 1, 12)).Single().Clone();
            exception.Title = "Obsolete edit";
            store.SaveOccurrence(exception);
            store.DeleteOccurrence(store.GetOnDay(new DateTime(2026, 1, 26)).Single());
            var edited = store.GetSeriesMaster(exception)!.Clone();
            if (change == "count") edited.RepeatCount = 1;
            if (change == "interval") edited.RepeatInterval = 2;
            if (change == "weekday") edited.RepeatDays = [DayOfWeek.Tuesday];
            if (change == "until") edited.RepeatUntil = new DateTime(2026, 1, 10);

            store.UpdateSeries(edited);

            Assert.Null(store.LoadError);
            Assert.Empty(store.GetOnDay(new DateTime(2026, 1, 12)));
            Assert.DoesNotContain(store.GetInRange(new DateTime(2026, 1, 1), new DateTime(2026, 3, 1)), e => e.Title == "Obsolete edit");
            Assert.Empty(store.GetById(edited.Id)!.SkipDates);
            fixture.Reopen();
            Assert.DoesNotContain(store.Events, e => e.IsOverride);
            Assert.Empty(store.GetById(edited.Id)!.SkipDates);
        }

        [Fact]
        public void MoveAcrossMidnightRebasesDeletedDatesByTheOccurrenceTime()
        {
            using var fixture = new CalendarFixture();
            var store = fixture.Store;
            AddWeeklySeries(store);
            var deleted = store.GetOnDay(new DateTime(2026, 1, 19)).Single();
            store.DeleteOccurrence(deleted);
            var edited = store.GetSeriesMaster(deleted)!.Clone();
            edited.Start = edited.Start.AddHours(23);
            edited.End = edited.End.AddHours(23);
            edited.RepeatDays = [DayOfWeek.Tuesday];

            store.UpdateSeries(edited);

            Assert.Null(store.LoadError);
            Assert.Empty(store.GetById(edited.Id)!.SkipDates);
            Assert.Equal(new[] { new DateTime(2026, 1, 20, 8, 0, 0) }, store.GetById(edited.Id)!.SkipOccurrences);
            Assert.Empty(store.GetOnDay(new DateTime(2026, 1, 20)));
            Assert.Equal(new DateTime(2026, 1, 13, 8, 0, 0), Assert.Single(store.GetOnDay(new DateTime(2026, 1, 13))).Start);
            fixture.Reopen();
            Assert.Empty(store.GetOnDay(new DateTime(2026, 1, 20)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedMasterOrOverrideWriteRollsBackTheWholeSeries(bool failOverride)
        {
            using var fixture = new CalendarFixture();
            var store = fixture.Store;
            var master = AddWeeklySeries(store);
            var exception = store.GetOnDay(new DateTime(2026, 1, 12)).Single().Clone();
            exception.Title = "Original edit";
            store.SaveOccurrence(exception);
            var edited = master.Clone();
            edited.Start = edited.Start.AddDays(1);
            edited.End = edited.End.AddDays(1);
            edited.RepeatDays = [DayOfWeek.Tuesday];
            fixture.Execute(failOverride
                ? "CREATE TRIGGER reject_write BEFORE UPDATE ON events WHEN OLD.series_id <> '' BEGIN SELECT RAISE(ABORT, 'override fault'); END"
                : "CREATE TRIGGER reject_write BEFORE UPDATE ON events WHEN OLD.series_id = '' BEGIN SELECT RAISE(ABORT, 'master fault'); END");
            int changes = 0;
            store.Changed += () => changes++;

            store.UpdateSeries(edited);

            Assert.NotNull(store.LoadError);
            Assert.Equal(1, changes);
            Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), master.Start);
            AssertOriginalSeries(store);
            fixture.Reopen();
            AssertOriginalSeries(store);
        }

        [Fact]
        public void UnrepresentableRebaseReportsFailureWithoutMutatingTheSeries()
        {
            using var fixture = new CalendarFixture();
            var store = fixture.Store;
            var master = AddWeeklySeries(store);
            var exception = store.GetOnDay(new DateTime(2026, 1, 12)).Single().Clone();
            exception.Title = "Original edit";
            store.SaveOccurrence(exception);
            var edited = master.Clone();
            edited.Start = new DateTime(9999, 12, 31, 9, 0, 0);
            edited.End = new DateTime(9999, 12, 31, 10, 0, 0);

            store.UpdateSeries(edited);

            Assert.NotNull(store.LoadError);
            AssertOriginalSeries(store);
            fixture.Reopen();
            AssertOriginalSeries(store);
        }

        private static void AssertOriginalSeries(EventStore store)
        {
            Assert.Equal("Series", Assert.Single(store.GetOnDay(new DateTime(2026, 1, 5))).Title);
            Assert.Equal("Original edit", Assert.Single(store.GetOnDay(new DateTime(2026, 1, 12))).Title);
            Assert.Empty(store.GetOnDay(new DateTime(2026, 1, 13)));
        }

        private static void AssertMovedException(EventStore store)
        {
            var occurrence = Assert.Single(store.GetOnDay(new DateTime(2026, 1, 13)));
            Assert.Equal("Personal edit", occurrence.Title);
            Assert.Equal(new DateTime(2026, 1, 13, 9, 0, 0), occurrence.OccurrenceStart);
            Assert.Equal(new DateTime(2026, 1, 13, 11, 0, 0), occurrence.Start);
            Assert.Equal(new DateTime(2026, 1, 13, 13, 0, 0), occurrence.End);
        }

        private static CalendarEvent AddWeeklySeries(EventStore store)
        {
            var master = new CalendarEvent
            {
                Title = "Series",
                Start = new DateTime(2026, 1, 5, 9, 0, 0),
                End = new DateTime(2026, 1, 5, 10, 0, 0),
                Repeat = RepeatFreq.Weekly,
                RepeatDays = [DayOfWeek.Monday]
            };
            store.Add(master);
            Assert.Null(store.LoadError);
            return master;
        }

        private sealed class CalendarFixture : IDisposable
        {
            private readonly string _root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\recurrence", Guid.NewGuid().ToString("N"));
            public string PathName { get; }
            public EventStore Store { get; } = new EventStore();

            public CalendarFixture()
            {
                Directory.CreateDirectory(_root);
                PathName = Path.Combine(_root, "calendar.kcal");
                Store.Open(PathName);
                Assert.Null(Store.LoadError);
            }

            public void Reopen()
            {
                Store.Close();
                Store.Open(PathName);
                Assert.Null(Store.LoadError);
            }

            public void Execute(string sql)
            {
                using var db = new SqliteConnection("Data Source=" + PathName);
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            public void Dispose()
            {
                Store.Close();
                Directory.Delete(_root, true);
            }
        }
    }
}
