using System;
using System.IO;
using System.Linq;
using System.Threading;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Findings from the KD-B adversarial review (undo, recently deleted, backups, lock,
    // reminders, search). Each test is named for what must hold.
    [Collection("Calendar settings")]
    public sealed class KdbReviewRegressionTests
    {
        private static readonly DateTime Now = new(2026, 10, 21, 9, 0, 0);

        private static CalendarEvent At(string title, DateTime start, int? reminder = null) => new()
            { Title = title, Start = start, End = start.AddHours(1), ReminderMinutes = reminder };

        private static string Alarm(string trigger) => string.Join("\r\n",
            "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:far@example.com",
            "DTSTART:20261022T090000", "DTEND:20261022T100000", "SUMMARY:Far alarm",
            "BEGIN:VALARM", "ACTION:DISPLAY", trigger, "END:VALARM",
            "END:VEVENT", "END:VCALENDAR", "");

        // BF-001: a locked calendar keeps nothing readable in memory.
        [Fact]
        public void LockDropsTheDecryptedAppointmentsFromMemory()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(At("Secret", Now));
            fixture.Store.SetPassword("pw");

            new SecurityController(new SecurityHostFake(), fixture.Store).Lock();

            Assert.Empty(fixture.Store.Events);
            Assert.Empty(fixture.Store.Search("secret", SearchSpan.Any, Now));
        }

        // BF-002: a lock that arrives while an import is writing waits instead of closing the
        // connection under it.
        [Fact]
        public void LockIsRefusedWhileAnImportIsWriting()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.SetPassword("pw");
            var security = new SecurityController(new SecurityHostFake(), fixture.Store);
            bool? lockedMidImport = null;

            var plan = fixture.Store.PlanImport([At("Imported", Now)]);
            var worker = new Thread(() => fixture.Store.WriteImport(plan, CancellationToken.None,
                () => lockedMidImport ??= security.Lock()));
            worker.Start();
            worker.Join();
            fixture.Store.PublishImport(plan);

            Assert.False(lockedMidImport);
            Assert.True(fixture.Store.IsOpen);
            Assert.Single(fixture.Store.Events);
        }

        // BF-003 and BF-015: an absurd or malformed alarm sets no reminder and never throws.
        [Theory]
        [InlineData("TRIGGER:-P800000D")]
        [InlineData("TRIGGER:-P99999999999999999999D")]
        [InlineData("TRIGGER:-P9999999999999999W")]
        public void AbsurdAlarmSetsNoReminder(string trigger)
            => Assert.Null(Assert.Single(IcsService.ParseText(Alarm(trigger)).Events).ReminderMinutes);

        [Fact]
        public void FourWeekAlarmIsTheLongestKept()
        {
            Assert.Equal(40320, Assert.Single(IcsService.ParseText(Alarm("TRIGGER:-P4W")).Events).ReminderMinutes);
            Assert.Null(Assert.Single(IcsService.ParseText(Alarm("TRIGGER:-P29D")).Events).ReminderMinutes);
        }

        [Fact]
        public void HugeStoredLeadDoesNotBreakTheReminderCheck()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(At("Corrupt lead", Now.AddDays(3), int.MaxValue));
            fixture.Store.Add(At("Normal", Now.AddMinutes(10), 15));

            Assert.Equal("Normal", Assert.Single(fixture.Store.DueReminders(Now)).Title);
            fixture.Store.UpcomingReminderTimes(Now, TimeSpan.FromDays(1));
        }

        // BF-004: undoing a restore puts the appointment back in Recently deleted.
        [Fact]
        public void UndoingARestoreFilesTheAppointmentAgain()
        {
            using var fixture = new AppointmentFixture();
            var ev = At("Restored then undone", Now);
            fixture.Store.Add(ev);
            fixture.Store.Delete(ev.Id);
            fixture.Store.RestoreDeleted(Assert.Single(fixture.Store.ListDeleted()).Id);

            Assert.True(fixture.Store.Undo());

            Assert.Empty(fixture.Store.Events);
            Assert.Equal("Restored then undone", Assert.Single(fixture.Store.ListDeleted()).Title);
        }

        // BF-013: deleting again after an undone delete lists the appointment once.
        [Fact]
        public void DeletingAgainAfterUndoListsItOnce()
        {
            using var fixture = new AppointmentFixture();
            var ev = At("Twice", Now);
            fixture.Store.Add(ev);
            fixture.Store.Delete(ev.Id);
            fixture.Store.Undo();
            fixture.Store.Delete(fixture.Store.Events.Single().Id);

            Assert.Single(fixture.Store.ListDeleted());
        }

        // BF-006: dismissing a series' reminder does not bring back its older missed date.
        [Fact]
        public void DismissingASeriesReminderKeepsItsOlderDatesQuiet()
        {
            using var fixture = new AppointmentFixture();
            var daily = At("Standup", new DateTime(2026, 10, 1, 9, 10, 0), 15);
            daily.Repeat = RepeatFreq.Daily;
            fixture.Store.Add(daily);

            fixture.Store.DismissReminder(Assert.Single(fixture.Store.DueReminders(Now)));

            Assert.Empty(fixture.Store.DueReminders(Now));
        }

        // BF-009: deleting a calendar deletes its backups, so a new one of the same name starts clean.
        [Fact]
        public void DeletedCalendarTakesItsBackupsWithIt()
        {
            string root = Path.Combine(TestScratch.Root, "backup-delete", Guid.NewGuid().ToString("N"));
            string? savedDir = Settings.Get("KillendarDataDir"), savedActive = Settings.Get("ActiveKillendar");
            var store = new EventStore();
            try
            {
                Directory.CreateDirectory(root);
                EventStore.SetDataDir(root);
                store.Open(Path.Combine(root, "Gone.kcal"));
                store.BackupNow(Now);
                store.Close();

                EventStore.DeleteKillendar("Gone.kcal");

                Assert.Empty(EventStore.ListBackupsFor(Path.Combine(root, "Gone.kcal")));
            }
            finally
            {
                store.Close();
                Settings.Set("KillendarDataDir", savedDir ?? "");
                Settings.Set("ActiveKillendar", savedActive ?? "");
                Directory.Delete(root, true);
            }
        }

        // BF-011: search shows a moved date of a series where it now is.
        [Fact]
        public void SearchSkipsADateThatWasMovedOnItsOwn()
        {
            using var fixture = new AppointmentFixture();
            var weekly = At("Planning", new DateTime(2026, 10, 1, 10, 0, 0));
            weekly.Repeat = RepeatFreq.Weekly;
            fixture.Store.Add(weekly);
            var next = fixture.Store.GetInRange(Now, Now.AddDays(8)).Single();
            var moved = next.Clone();
            moved.Start = next.Start.AddHours(5);
            moved.End = next.End.AddHours(5);
            fixture.Store.SaveOccurrence(moved);

            var found = fixture.Store.Search("planning", SearchSpan.Upcoming, Now);

            Assert.DoesNotContain(found, e => e.Start == next.Start);
            Assert.Contains(found, e => e.Start == moved.Start);
        }

        // BF-012: while locked, reload and the other ways back in do nothing until the unlock.
        [Fact]
        public void ReloadWhileLockedLeavesTheCalendarLocked()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.SetPassword("pw");
            var security = new SecurityController(new SecurityHostFake(), fixture.Store);
            security.Lock();

            security.ReloadActive();
            security.ToggleLock();

            Assert.True(security.IsLocked);
            Assert.False(fixture.Store.IsOpen);
        }
    }
}
