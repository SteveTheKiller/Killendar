using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S15: duplicate, templates, overlap warnings and moving an appointment from the keyboard.
    [Collection("Calendar settings")]
    public sealed class DailyEntryRegressionTests
    {
        private static CalendarEvent Meeting(string title, DateTime start, int hours = 1) => new()
            { Title = title, Start = start, End = start.AddHours(hours) };

        [Fact]
        public void DuplicateSavesANewAppointmentWithoutTheOriginalsIdentity()
        {
            using var fixture = new AppointmentFixture();
            var original = Meeting("Imported", new DateTime(2026, 10, 22, 9, 0, 0));
            original.ExternalUid = "meeting@example.com";
            original.KeptProperties = ["ORGANIZER:mailto:boss@example.com", "SEQUENCE:3"];
            original.Attendees = ["sam@example.com"];
            fixture.Store.Add(original);
            fixture.Editor.Load(fixture.Store.Events.Single());

            fixture.Editor.Duplicate();
            fixture.Editor.Save();

            Assert.Equal(2, fixture.Store.Events.Count);
            var copy = fixture.Store.Events.Single(e => e.Id != original.Id);
            Assert.Equal("Imported", copy.Title);
            Assert.Equal("", copy.ExternalUid);
            Assert.Empty(copy.KeptProperties);
            Assert.Equal(original.Start, copy.Start);
        }

        [Fact]
        public void DuplicateCountsAsUnsavedInput()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Meeting("Original", new DateTime(2026, 10, 22, 9, 0, 0)));
            fixture.Editor.Load(fixture.Store.Events.Single());

            fixture.Editor.Duplicate();

            Assert.True(fixture.Editor.HasUnsavedChanges);
        }

        [Fact]
        public void TemplatesKeepDetailsButNotPeopleOrDates()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 14, 30, 0));
            fixture.View.FieldTitle = "Standup";
            fixture.View.FieldLocation = "Room 4";
            fixture.View.FieldAttendees = "sam@example.com";
            fixture.View.FieldReminder = 5;
            fixture.View.FieldEndTime = "3:15 PM";

            Assert.True(fixture.Editor.SaveTemplate());
            var template = Assert.Single(fixture.Store.ListTemplates());
            Assert.Empty(template.Attendees);

            fixture.Editor.Clear();
            fixture.Editor.NewFromTemplate(template, new DateTime(2026, 11, 3));
            fixture.Editor.Save();

            var made = Assert.Single(fixture.Store.Events);
            Assert.Equal(new DateTime(2026, 11, 3, 14, 30, 0), made.Start);
            Assert.Equal(new DateTime(2026, 11, 3, 15, 15, 0), made.End);
            Assert.Equal("Room 4", made.Location);
            Assert.Equal(5, made.ReminderMinutes);
            Assert.Empty(made.Attendees);
        }

        [Fact]
        public void TemplatesAreReplacedByNameAndCanBeDeleted()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.SaveTemplate(Meeting("Review", new DateTime(2026, 10, 22, 9, 0, 0)));
            fixture.Store.SaveTemplate(Meeting("Review", new DateTime(2026, 10, 22, 10, 0, 0)));
            Assert.Equal(10, Assert.Single(fixture.Store.ListTemplates()).Start.Hour);

            fixture.Store.DeleteTemplate("Review");

            Assert.Empty(fixture.Store.ListTemplates());
        }

        [Fact]
        public void OverlapsAreReportedNotBlocked()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Meeting("Dentist", new DateTime(2026, 10, 22, 9, 30, 0)));
            var free = Meeting("Focus time", new DateTime(2026, 10, 22, 9, 0, 0));
            free.KeptProperties = ["TRANSP:TRANSPARENT"];
            fixture.Store.Add(free);
            fixture.Store.Add(new CalendarEvent { Title = "Holiday", Start = new DateTime(2026, 10, 22), End = new DateTime(2026, 10, 23), AllDay = true });
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldTitle = "Standup";

            fixture.Editor.Save();

            Assert.Equal(4, fixture.Store.Events.Count);
            Assert.EndsWith("Str_Status_Overlaps", fixture.View.LastStatus);   // one overlap, not "and N more"
            var standup = fixture.Store.Events.Single(e => e.Title == "Standup");
            Assert.Equal("Dentist", Assert.Single(fixture.Store.Overlapping(standup)).Title);
        }

        [Fact]
        public void KeyboardMoveShiftsStartAndEndTogether()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldTitle = "Moved";

            fixture.Editor.Nudge(TimeSpan.FromMinutes(15));
            fixture.Editor.Nudge(TimeSpan.FromDays(1));
            fixture.Editor.Save();

            var made = Assert.Single(fixture.Store.Events);
            Assert.Equal(new DateTime(2026, 10, 23, 9, 15, 0), made.Start);
            Assert.Equal(new DateTime(2026, 10, 23, 10, 15, 0), made.End);
        }

        // KD-C review BF-019: a template from a three-day all-day appointment makes three days.
        [Fact]
        public void AMultiDayAllDayTemplateKeepsItsDays()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 20));
            fixture.View.FieldTitle = "Conference";
            fixture.Editor.ToggleAllDay();
            fixture.View.FieldEndDate = DateFormatManager.Format(new DateTime(2026, 10, 22));
            Assert.True(fixture.Editor.SaveTemplate());

            var template = Assert.Single(fixture.Store.ListTemplates());
            Assert.Equal(TimeSpan.FromDays(3), template.End - template.Start);
        }
    }
}
