using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S08: a mistaken move, edit, delete or import comes back with Ctrl+Z, saved as it was.
    [Collection("Calendar settings")]
    public sealed class UndoRegressionTests
    {
        private static CalendarEvent Appointment(string title, int day = 22) => new()
        {
            Title = title,
            Start = new DateTime(2026, 10, day, 9, 0, 0),
            End = new DateTime(2026, 10, day, 10, 0, 0),
        };

        [Fact]
        public void EditUndoesAndRedoesAndBothAreSaved()
        {
            using var fixture = new AppointmentFixture();
            var ev = Appointment("Original");
            fixture.Store.Add(ev);
            var edited = ev.Clone();
            edited.Title = "Edited";
            fixture.Store.Update(edited);

            Assert.True(fixture.Store.Undo());
            Assert.Equal("Original", Reopened(fixture).Single().Title);
            Assert.True(fixture.Store.CanRedo == false);
        }

        [Fact]
        public void RedoReappliesTheEdit()
        {
            using var fixture = new AppointmentFixture();
            var ev = Appointment("Original");
            fixture.Store.Add(ev);
            var edited = ev.Clone();
            edited.Title = "Edited";
            fixture.Store.Update(edited);

            fixture.Store.Undo();
            Assert.True(fixture.Store.Redo());

            Assert.Equal("Edited", Assert.Single(fixture.Store.Events).Title);
            Assert.Equal("Edited", Reopened(fixture).Single().Title);
        }

        [Fact]
        public void DeleteUndoesBackToTheSameAppointment()
        {
            using var fixture = new AppointmentFixture();
            var ev = Appointment("Keep me");
            fixture.Store.Add(ev);
            fixture.Store.Delete(ev.Id);

            Assert.True(fixture.Store.Undo());

            var back = Assert.Single(fixture.Store.Events);
            Assert.Equal(ev.Id, back.Id);
            Assert.Equal("Keep me", Reopened(fixture).Single().Title);
        }

        [Fact]
        public void AddUndoesAway()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Mistake"));

            Assert.True(fixture.Store.Undo());

            Assert.Empty(fixture.Store.Events);
            Assert.Empty(Reopened(fixture));
        }

        [Fact]
        public void SeriesEditUndoesWithItsSingleDateEdits()
        {
            using var fixture = new AppointmentFixture();
            var series = Appointment("Standup", 19);
            series.Repeat = RepeatFreq.Daily;
            series.RepeatCount = 5;
            fixture.Store.Add(series);
            var occurrence = fixture.Store.GetOnDay(new DateTime(2026, 10, 21)).Single().Clone();
            occurrence.Title = "Late standup";
            fixture.Store.SaveOccurrence(occurrence);
            var whole = fixture.Store.GetOnDay(new DateTime(2026, 10, 20)).Single().Clone();
            whole.Start = whole.Start.AddHours(2);
            whole.End = whole.End.AddHours(2);
            fixture.Store.UpdateSeries(whole);

            Assert.True(fixture.Store.Undo());

            var days = fixture.Store.GetInRange(new DateTime(2026, 10, 19), new DateTime(2026, 10, 24));
            Assert.All(days, d => Assert.Equal(9, d.Start.Hour));
            Assert.Contains(days, d => d.Title == "Late standup");
        }

        [Fact]
        public void ImportUndoesAsOneStep()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Before"));
            fixture.Store.ImportEvents(Enumerable.Range(1, 30).Select(i => Appointment("Imported " + i, i % 28 + 1)));

            Assert.True(fixture.Store.Undo());

            Assert.Equal("Before", Assert.Single(fixture.Store.Events).Title);
            Assert.Single(Reopened(fixture));
        }

        [Fact]
        public void HistoryKeepsTheLastFiftySteps()
        {
            using var fixture = new AppointmentFixture();
            for (int i = 0; i < 55; i++) fixture.Store.Add(Appointment("A" + i, i % 28 + 1));

            int undone = 0;
            while (fixture.Store.Undo()) undone++;

            Assert.Equal(50, undone);
            Assert.Equal(5, fixture.Store.Events.Count);
        }

        [Fact]
        public void NewChangeClearsRedo()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("One"));
            fixture.Store.Undo();
            fixture.Store.Add(Appointment("Two"));

            Assert.False(fixture.Store.CanRedo);
            Assert.False(fixture.Store.Redo());
        }

        [Fact]
        public void ReopeningOrCategoryChangesForgetTheHistory()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("One"));
            Assert.True(fixture.Store.CanUndo);

            fixture.Store.AddCategory("Travel", "#123456");
            Assert.False(fixture.Store.CanUndo);

            fixture.Store.Add(Appointment("Two"));
            Reopened(fixture);
            Assert.False(fixture.Store.CanUndo);
        }

        [Fact]
        public void NothingToUndoReturnsFalse()
        {
            using var fixture = new AppointmentFixture();
            Assert.False(fixture.Store.CanUndo);
            Assert.False(fixture.Store.Undo());
        }

        private static System.Collections.Generic.IReadOnlyList<CalendarEvent> Reopened(AppointmentFixture fixture)
        {
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);
            return fixture.Store.Events;
        }
    }
}
