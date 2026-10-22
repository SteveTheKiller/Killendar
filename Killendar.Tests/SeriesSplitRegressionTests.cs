using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S14: "this and following" splits a series in two, carrying later single-date edits and
    // skipped dates with the second half.
    [Collection("Calendar settings")]
    public sealed class SeriesSplitRegressionTests
    {
        private static readonly DateTime First = new(2026, 10, 19, 9, 0, 0);

        private static CalendarEvent Daily(int count = 0) => new()
        {
            Title = "Standup", Start = First, End = First.AddMinutes(30), Repeat = RepeatFreq.Daily, RepeatCount = count,
        };

        private static CalendarEvent OccurrenceOn(EventStore store, DateTime day) =>
            store.GetInRange(day.Date, day.Date.AddDays(1)).Single(o => o.Title.StartsWith("Standup") || o.Title == "Retro");

        [Fact]
        public void EditingFollowingChangesThatDateOnward()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily());
            var third = OccurrenceOn(fixture.Store, First.AddDays(2)).Clone();
            third.Title = "Retro";
            third.Start = third.Start.AddHours(1);
            third.End = third.End.AddHours(1);

            Assert.True(fixture.Store.CanSplit(third));
            fixture.Store.UpdateFollowing(third);

            var week = fixture.Store.GetInRange(First.Date, First.Date.AddDays(5)).OrderBy(o => o.Start).ToList();
            Assert.Equal(["Standup", "Standup", "Retro", "Retro", "Retro"], week.Select(o => o.Title));
            Assert.Equal(10, week[2].Start.Hour);
            Assert.Equal(9, week[1].Start.Hour);
            Assert.Equal(2, fixture.Store.Events.Count(e => e.IsSeries));
        }

        [Fact]
        public void CountIsSharedBetweenTheHalves()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily(count: 5));
            var third = OccurrenceOn(fixture.Store, First.AddDays(2)).Clone();
            third.Title = "Retro";

            fixture.Store.UpdateFollowing(third);

            var all = fixture.Store.GetInRange(First.Date, First.Date.AddDays(30));
            Assert.Equal(5, all.Count);
            Assert.Equal(3, all.Count(o => o.Title == "Retro"));
        }

        [Fact]
        public void LaterEditsAndSkipsGoWithTheSecondHalf()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily());
            var fourth = OccurrenceOn(fixture.Store, First.AddDays(3)).Clone();
            fourth.Location = "Room 7";
            fixture.Store.SaveOccurrence(fourth);
            fixture.Store.DeleteOccurrence(OccurrenceOn(fixture.Store, First.AddDays(4)));
            var second = OccurrenceOn(fixture.Store, First.AddDays(1)).Clone();
            second.Title = "Retro";

            fixture.Store.UpdateFollowing(second);

            var days = fixture.Store.GetInRange(First.Date, First.Date.AddDays(6)).OrderBy(o => o.Start).ToList();
            Assert.Equal(5, days.Count);                         // the skipped fifth day stays skipped
            var edited = days.Single(o => o.Start.Date == First.AddDays(3).Date);
            Assert.Equal("Room 7", edited.Location);             // a date edited on its own keeps its edit
            Assert.True(edited.IsOverride);
            Assert.Equal("Retro", days.Single(o => o.Start.Date == First.AddDays(2).Date).Title);
        }

        [Fact]
        public void DeletingFollowingEndsTheSeriesBeforeThatDate()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily());

            fixture.Store.DeleteFollowing(OccurrenceOn(fixture.Store, First.AddDays(3)));

            var all = fixture.Store.GetInRange(First.Date, First.Date.AddDays(60));
            Assert.Equal(3, all.Count);
            Assert.True(fixture.Store.Undo());
            Assert.Equal(60, fixture.Store.GetInRange(First.Date, First.Date.AddDays(60)).Count);
        }

        [Fact]
        public void SplittingAtTheFirstDateIsAWholeSeriesEdit()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily());
            var first = OccurrenceOn(fixture.Store, First).Clone();
            first.Title = "Retro";

            fixture.Store.UpdateFollowing(first);

            Assert.Single(fixture.Store.Events);
            Assert.Equal("Retro", fixture.Store.Events[0].Title);
        }

        [Fact]
        public void OneUndoPutsTheSeriesBack()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily());
            var third = OccurrenceOn(fixture.Store, First.AddDays(2)).Clone();
            third.Title = "Retro";
            fixture.Store.UpdateFollowing(third);

            Assert.True(fixture.Store.Undo());

            Assert.Single(fixture.Store.Events);
            Assert.All(fixture.Store.GetInRange(First.Date, First.Date.AddDays(5)), o => Assert.Equal("Standup", o.Title));
        }

        [Fact]
        public void EditorSavesThisAndFollowing()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily());
            fixture.Editor.Load(OccurrenceOn(fixture.Store, First.AddDays(2)));
            fixture.View.EditFollowing = true;
            fixture.View.FieldTitle = "Retro";

            fixture.Editor.Save();

            Assert.Null(fixture.View.Error);
            Assert.Equal(2, fixture.Store.Events.Count);
            var titles = fixture.Store.GetInRange(First.Date, First.Date.AddDays(5)).OrderBy(o => o.Start).Select(o => o.Title).ToList();
            Assert.Equal(["Standup", "Standup", "Retro", "Retro", "Retro"], titles);
        }

        // KD-C review BF-001: a split that could not be written must not fall back to the whole
        // series, and a delete that fails after the split must put the series back.
        [Fact]
        public void AFailedSplitChangesNothing()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily());
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.OpenReadOnly(path);
            var third = OccurrenceOn(fixture.Store, First.AddDays(2));

            fixture.Store.DeleteFollowing(third);

            Assert.NotNull(fixture.Store.LoadError);
            Assert.Equal(5, fixture.Store.GetInRange(First.Date, First.Date.AddDays(5)).Count);
            Assert.False(fixture.Store.CanUndo);
        }

        [Fact]
        public void ADeleteThatFailsAfterTheSplitPutsTheSeriesBack()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Daily());
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + fixture.Store.FilePath + ";Pooling=False"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "CREATE TRIGGER no_trash BEFORE INSERT ON deleted BEGIN SELECT RAISE(ABORT, 'disk full'); END";
                cmd.ExecuteNonQuery();
            }
            var third = OccurrenceOn(fixture.Store, First.AddDays(2));

            fixture.Store.DeleteFollowing(third);

            Assert.Contains("disk full", fixture.Store.LoadError);
            var master = Assert.Single(fixture.Store.Events);
            Assert.Equal(0, master.RepeatCount);
            Assert.Null(master.RepeatUntil);
            Assert.Equal(5, fixture.Store.GetInRange(First.Date, First.Date.AddDays(5)).Count);
            Assert.False(fixture.Store.CanUndo);
        }
    }
}
