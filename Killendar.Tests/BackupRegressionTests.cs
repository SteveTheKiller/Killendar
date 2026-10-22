using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    // S08: a verified snapshot beside the calendar, once a day and before risky changes, kept
    // seven days and four weeks back, encrypted with the calendar's own password.
    [Collection("Calendar settings")]
    public sealed class BackupRegressionTests
    {
        private static CalendarEvent Appointment(string title) => new()
        {
            Title = title,
            Start = new DateTime(2026, 10, 22, 9, 0, 0),
            End = new DateTime(2026, 10, 22, 10, 0, 0),
        };

        [Fact]
        public void BackupIsAVerifiedCopyBesideTheCalendar()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Kept"));

            var backup = fixture.Store.BackupNow(new DateTime(2026, 10, 21, 9, 30, 0));

            Assert.NotNull(backup);
            Assert.Equal(Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, "Backups"), Path.GetDirectoryName(backup));
            var copy = new EventStore();
            try
            {
                copy.OpenReadOnly(backup!);
                Assert.Equal("Kept", Assert.Single(copy.Events).Title);
            }
            finally { copy.Close(); }
            Assert.DoesNotContain(EventStore.ListKillendars(), name => name.Contains("Backups"));
        }

        [Fact]
        public void EncryptedCalendarBackupNeedsItsPassword()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Secret"));
            fixture.Store.SetPassword("pw");

            string backup = fixture.Store.BackupNow(new DateTime(2026, 10, 21, 9, 30, 0))!;

            Assert.True(EventStore.IsEncryptedFile(backup));
            using var keyed = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = backup, Password = "pw", Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            keyed.Open();
            using var count = keyed.CreateCommand();
            count.CommandText = "SELECT count(*) FROM events";
            Assert.Equal(1L, count.ExecuteScalar());
        }

        [Fact]
        public void PasswordChangeBacksUpFirstAndLeavesNoReadableCopy()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Private"));
            fixture.Store.BackupNow(new DateTime(2026, 10, 20, 9, 0, 0));

            fixture.Store.SetPassword("pw");

            var backups = fixture.Store.ListBackups();
            Assert.Equal(2, backups.Count);   // the earlier one and the one taken just before the change
            Assert.All(backups, b => Assert.True(EventStore.IsEncryptedFile(b.Path)));
            Assert.Null(fixture.Store.LastBackupError);
        }

        [Fact]
        public void ImportBacksUpFirst()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Before"));

            var plan = fixture.Store.PlanImport([Appointment("Imported")]);
            fixture.Store.WriteImport(plan, System.Threading.CancellationToken.None);
            fixture.Store.PublishImport(plan);

            var copy = new EventStore();
            try
            {
                copy.OpenReadOnly(Assert.Single(fixture.Store.ListBackups()).Path);
                Assert.Equal("Before", Assert.Single(copy.Events).Title);
            }
            finally { copy.Close(); }
        }

        [Fact]
        public void DailyBackupRunsBesideTheAppWithItsOwnConnection()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Before"));
            fixture.Store.SetPassword("pw");
            foreach (var old in fixture.Store.ListBackups()) File.Delete(old.Path);

            var backup = fixture.Store.BackupIfDueAsync(new DateTime(2026, 10, 21, 9, 0, 0));
            fixture.Store.Add(Appointment("While it runs"));
            string? path = backup.Result;

            Assert.True(fixture.Store.LastBackupError == null, fixture.Store.LastBackupError);
            Assert.NotNull(path);
            Assert.True(EventStore.IsEncryptedFile(path!));
            Assert.Equal(2, fixture.Store.Events.Count);
            Assert.Null(fixture.Store.BackupIfDueAsync(new DateTime(2026, 10, 21, 18, 0, 0)).Result);
        }

        [Fact]
        public void DailyBackupRunsOncePerDay()
        {
            using var fixture = new AppointmentFixture();
            var morning = new DateTime(2026, 10, 21, 8, 0, 0);

            Assert.NotNull(fixture.Store.BackupIfDue(morning));
            Assert.Null(fixture.Store.BackupIfDue(morning.AddHours(6)));
            Assert.NotNull(fixture.Store.BackupIfDue(morning.AddDays(1)));
            Assert.Equal(2, fixture.Store.ListBackups().Count);
        }

        [Fact]
        public void RetentionKeepsSevenDaysAndFourWeeks()
        {
            using var fixture = new AppointmentFixture();
            var start = new DateTime(2026, 9, 1, 9, 0, 0);
            for (int day = 0; day < 50; day++)
                fixture.Store.BackupNow(start.AddDays(day));

            var kept = fixture.Store.ListBackups().Select(b => b.Taken.Date).ToList();

            var last = start.AddDays(49).Date;
            for (int day = 0; day < 7; day++) Assert.Contains(last.AddDays(-day), kept);
            Assert.Equal(11, kept.Count);
            Assert.True(kept.Min() >= last.AddDays(-7 * 5));
        }

        [Fact]
        public void BackupThatCannotBeWrittenLeavesEarlierOnesAlone()
        {
            using var fixture = new AppointmentFixture();
            string first = fixture.Store.BackupNow(new DateTime(2026, 10, 20, 9, 0, 0))!;
            string blocker = Path.Combine(Path.GetDirectoryName(first)!,
                Path.GetFileNameWithoutExtension(fixture.Store.FilePath) + " 2026-10-21 090000.kcal");
            Directory.CreateDirectory(blocker);   // a folder where the new backup would go

            Assert.Null(fixture.Store.BackupNow(new DateTime(2026, 10, 21, 9, 0, 0)));

            Assert.True(File.Exists(first));
            Assert.NotNull(fixture.Store.LastBackupError);
        }

        [Fact]
        public void RenamedCalendarKeepsItsBackups()
        {
            string root = Path.Combine(TestScratch.Root, "backup-rename", Guid.NewGuid().ToString("N"));
            string? savedDir = Settings.Get("KillendarDataDir"), savedActive = Settings.Get("ActiveKillendar");
            var store = new EventStore();
            try
            {
                Directory.CreateDirectory(root);
                EventStore.SetDataDir(root);
                store.Open(Path.Combine(root, "Work.kcal"));
                store.BackupNow(new DateTime(2026, 10, 21, 9, 0, 0));
                store.Close();

                EventStore.RenameKillendar("Work.kcal", "Office.kcal");

                Assert.Empty(EventStore.ListBackupsFor(Path.Combine(root, "Work.kcal")));
                Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0),
                    Assert.Single(EventStore.ListBackupsFor(Path.Combine(root, "Office.kcal"))).Taken);
            }
            finally
            {
                store.Close();
                EventStore.LastRename = null;
                Settings.Set("KillendarDataDir", savedDir ?? "");
                Settings.Set("ActiveKillendar", savedActive ?? "");
                Directory.Delete(root, true);
            }
        }

        // KD-C review BF-014: the backup's own outcome comes back with its task.
        [Fact]
        public void TheBackgroundBackupReportsItsOwnOutcome()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Before"));
            foreach (var old in fixture.Store.ListBackups()) File.Delete(old.Path);

            var (path, error) = fixture.Store.BackupIfDueWithErrorAsync(new DateTime(2026, 10, 21, 9, 0, 0)).Result;

            Assert.Null(error);
            Assert.True(File.Exists(path));
            Assert.Equal((null, null), fixture.Store.BackupIfDueWithErrorAsync(new DateTime(2026, 10, 21, 18, 0, 0)).Result);
        }

        // Every table is copied, not just appointments: templates and Recently deleted come along.
        [Fact]
        public void TheBackupHoldsEveryTable()
        {
            using var fixture = new AppointmentFixture();
            var gone = Appointment("Deleted");
            fixture.Store.Add(Appointment("Kept"));
            fixture.Store.Add(gone);
            fixture.Store.Delete(gone.Id);
            fixture.Store.SaveTemplate(Appointment("Template"));
            foreach (var old in fixture.Store.ListBackups()) File.Delete(old.Path);

            var (path, error) = fixture.Store.BackupIfDueWithErrorAsync(new DateTime(2026, 10, 21, 9, 0, 0)).Result;

            Assert.Null(error);
            var copy = new EventStore();
            copy.OpenReadOnly(path!);
            try
            {
                Assert.Equal("Kept", Assert.Single(copy.Events).Title);
                Assert.Equal("Template", Assert.Single(copy.ListTemplates()).Title);
            }
            finally { copy.Close(); }
        }
    }
}
