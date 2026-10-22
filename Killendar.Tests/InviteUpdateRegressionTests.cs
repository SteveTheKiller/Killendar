using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-D: a newer invite updates the copy already here, and a cancellation removes it.
    [Collection("Calendar settings")]
    public sealed class InviteUpdateRegressionTests
    {
        private static string Invite(int sequence, string start, string title, string? extra = null, string? method = null)
        {
            var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0" };
            if (method != null) lines.Add("METHOD:" + method);
            lines.AddRange(new[] { "BEGIN:VEVENT", "UID:planning@example.com", "SEQUENCE:" + sequence,
                "DTSTART:" + start, "DTEND:" + start.Substring(0, 9) + "100000", "SUMMARY:" + title,
                "ORGANIZER:mailto:boss@example.com", "ATTENDEE:mailto:me@example.com" });
            if (extra != null) lines.Add(extra);
            lines.AddRange(new[] { "END:VEVENT", "END:VCALENDAR", "" });
            return string.Join("\r\n", lines);
        }

        private static ImportPlan Import(EventStore store, string ics)
        {
            var parsed = IcsService.ParseText(ics);
            var plan = store.PlanImport(parsed.Events, parsed.CanceledIds, parsed.CanceledOccurrences);
            store.WriteImport(plan, CancellationToken.None);
            store.PublishImport(plan);
            return plan;
        }

        [Fact]
        public void TheSameInviteAgainIsSkipped()
        {
            using var fixture = new AppointmentFixture();
            Import(fixture.Store, Invite(0, "20261022T090000", "Planning"));

            var plan = Import(fixture.Store, Invite(0, "20261022T090000", "Planning"));

            Assert.Equal(1, plan.AlreadyPresent);
            Assert.False(plan.ChangesExisting);
        }

        [Fact]
        public void ANewerInviteUpdatesAndKeepsWhatWasChosenHere()
        {
            using var fixture = new AppointmentFixture();
            Import(fixture.Store, Invite(0, "20261022T090000", "Planning"));
            var mine = fixture.Store.Events[0].Clone();
            mine.Categories = "Work";
            mine.ReminderMinutes = 30;
            fixture.Store.Update(mine);

            var plan = Import(fixture.Store, Invite(1, "20261023T090000", "Planning, moved"));

            Assert.Equal(1, plan.NewerInvites);
            var now = Assert.Single(fixture.Store.Events);
            Assert.Equal("Planning, moved", now.Title);
            Assert.Equal(new DateTime(2026, 10, 23, 9, 0, 0), now.Start);
            Assert.Equal("Work", now.Categories);
            Assert.Equal(30, now.ReminderMinutes);

            Assert.True(fixture.Store.Undo());
            Assert.Equal("Planning", Assert.Single(fixture.Store.Events).Title);
        }

        [Fact]
        public void AnOlderInviteDoesNotUndoAChange()
        {
            using var fixture = new AppointmentFixture();
            Import(fixture.Store, Invite(2, "20261023T090000", "Planning, moved"));

            var plan = Import(fixture.Store, Invite(1, "20261022T090000", "Planning"));

            Assert.Equal(1, plan.AlreadyPresent);
            Assert.Equal("Planning, moved", Assert.Single(fixture.Store.Events).Title);
        }

        [Fact]
        public void ACancellationRemovesTheAppointmentToRecentlyDeleted()
        {
            using var fixture = new AppointmentFixture();
            Import(fixture.Store, Invite(0, "20261022T090000", "Planning"));

            var plan = Import(fixture.Store, Invite(1, "20261022T090000", "Planning", "STATUS:CANCELLED", method: "CANCEL"));

            Assert.Equal(1, plan.Cancellations);
            Assert.Empty(fixture.Store.Events);
            Assert.Contains(fixture.Store.ListDeleted(), d => d.Title == "Planning");
            Assert.True(fixture.Store.Undo());
            Assert.Single(fixture.Store.Events);
        }

        [Fact]
        public void ACanceledDateRemovesJustThatDateOfTheSeries()
        {
            using var fixture = new AppointmentFixture();
            Import(fixture.Store, Invite(0, "20261019T090000", "Standup", "RRULE:FREQ=DAILY;COUNT=5"));
            string cancel = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "METHOD:CANCEL", "BEGIN:VEVENT",
                "UID:planning@example.com", "RECURRENCE-ID:20261021T090000", "SEQUENCE:1", "STATUS:CANCELLED",
                "DTSTART:20261021T090000", "DTEND:20261021T100000", "SUMMARY:Standup", "END:VEVENT", "END:VCALENDAR", "");

            var plan = Import(fixture.Store, cancel);

            Assert.Equal(1, plan.Cancellations);
            var dates = fixture.Store.GetInRange(new DateTime(2026, 10, 19), new DateTime(2026, 10, 26)).Select(o => o.Start.Day).ToList();
            Assert.Equal([19, 20, 22, 23], dates);
        }

        [Fact]
        public void ACancellationForSomethingNotHereChangesNothing()
        {
            using var fixture = new AppointmentFixture();
            var plan = Import(fixture.Store, Invite(1, "20261022T090000", "Planning", "STATUS:CANCELLED", method: "CANCEL"));
            Assert.Equal(0, plan.Cancellations);
            Assert.False(plan.HasWork);
        }
    }
}
