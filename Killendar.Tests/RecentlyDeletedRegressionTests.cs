using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S08: a deleted appointment or series is kept inside the calendar file for 30 days and can be
    // put back exactly as it was, after a restart too.
    [Collection("Calendar settings")]
    public sealed class RecentlyDeletedRegressionTests
    {
        private static CalendarEvent Appointment(string title, int day = 22) => new()
        {
            Title = title,
            Start = new DateTime(2026, 10, day, 9, 0, 0),
            End = new DateTime(2026, 10, day, 10, 0, 0),
        };

        [Fact]
        public void DeletedAppointmentIsListedAndRestoresAfterReopen()
        {
            using var fixture = new AppointmentFixture();
            var ev = Appointment("Dentist");
            fixture.Store.Add(ev);
            fixture.Store.Delete(ev.Id);
            Reopen(fixture);

            var item = Assert.Single(fixture.Store.ListDeleted());
            Assert.Equal("Dentist", item.Title);
            Assert.True(fixture.Store.RestoreDeleted(item.Id));

            Assert.Equal(ev.Id, Assert.Single(fixture.Store.Events).Id);
            Assert.Empty(fixture.Store.ListDeleted());
            Reopen(fixture);
            Assert.Equal("Dentist", Assert.Single(fixture.Store.Events).Title);
        }

        [Fact]
        public void DeletedSeriesComesBackWithItsSingleDateEdits()
        {
            using var fixture = new AppointmentFixture();
            var series = Appointment("Standup", 19);
            series.Repeat = RepeatFreq.Daily;
            series.RepeatCount = 5;
            fixture.Store.Add(series);
            var one = fixture.Store.GetOnDay(new DateTime(2026, 10, 21)).Single().Clone();
            one.Title = "Late standup";
            fixture.Store.SaveOccurrence(one);
            fixture.Store.DeleteSeries(series.Id);

            var item = Assert.Single(fixture.Store.ListDeleted());
            Assert.Equal(2, item.Rows);
            fixture.Store.RestoreDeleted(item.Id);

            var days = fixture.Store.GetInRange(new DateTime(2026, 10, 19), new DateTime(2026, 10, 24));
            Assert.Equal(5, days.Count);
            Assert.Contains(days, d => d.Title == "Late standup");
        }

        [Fact]
        public void EntriesOlderThanThirtyDaysArePurged()
        {
            using var fixture = new AppointmentFixture();
            var ev = Appointment("Old");
            fixture.Store.Add(ev);
            fixture.Store.Delete(ev.Id);

            fixture.Store.PurgeDeleted(DateTime.UtcNow.AddDays(29));
            Assert.Single(fixture.Store.ListDeleted());
            fixture.Store.PurgeDeleted(DateTime.UtcNow.AddDays(31));
            Assert.Empty(fixture.Store.ListDeleted());
        }

        [Fact]
        public void AppointmentAlreadyBackByUndoIsNotListedOrDuplicated()
        {
            using var fixture = new AppointmentFixture();
            var ev = Appointment("Back");
            fixture.Store.Add(ev);
            fixture.Store.Delete(ev.Id);
            fixture.Store.Undo();

            Assert.Empty(fixture.Store.ListDeleted());
            Assert.Single(fixture.Store.Events);
        }

        [Fact]
        public void RestoreCanBeUndone()
        {
            using var fixture = new AppointmentFixture();
            var ev = Appointment("Twice");
            fixture.Store.Add(ev);
            fixture.Store.Delete(ev.Id);
            fixture.Store.RestoreDeleted(fixture.Store.ListDeleted().Single().Id);

            Assert.True(fixture.Store.Undo());

            Assert.Empty(fixture.Store.Events);
        }

        [Fact]
        public void RemovingOneDateOfASeriesIsNotTrash()
        {
            using var fixture = new AppointmentFixture();
            var series = Appointment("Standup", 19);
            series.Repeat = RepeatFreq.Daily;
            series.RepeatCount = 5;
            fixture.Store.Add(series);
            fixture.Store.DeleteOccurrence(fixture.Store.GetOnDay(new DateTime(2026, 10, 21)).Single());

            Assert.Empty(fixture.Store.ListDeleted());
        }

        private static void Reopen(AppointmentFixture fixture)
        {
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);
        }
    }
}
