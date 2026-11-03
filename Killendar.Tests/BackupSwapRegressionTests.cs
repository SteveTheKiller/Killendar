using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // SF-002, SF-006, SF-007 and SF-010: a failed backup swap never loses the only copy, a
    // committed password change survives a failed reopen, an archived calendar takes its backups
    // with it, and a lock cannot slip in between an import being queued and running.
    [Collection("Calendar settings")]
    public sealed class BackupSwapRegressionTests
    {
        private static CalendarEvent Appointment(string title) => new()
        {
            Title = title,
            Start = new DateTime(2026, 10, 22, 9, 0, 0),
            End = new DateTime(2026, 10, 22, 10, 0, 0),
        };

        [Fact]
        public void FailedRekeySwapKeepsTheOriginalBackup()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Kept"));
            fixture.Store.BackupNow(new DateTime(2026, 10, 21, 9, 30, 0));
            fixture.Store.RekeyMove = (source, destination) =>
            {
                if (source.EndsWith(".rekey", StringComparison.OrdinalIgnoreCase)) throw new IOException("injected move failure");
                File.Move(source, destination);
            };

            fixture.Store.SetPassword("pw");

            Assert.Equal(2, fixture.Store.BackupsNotRekeyed.Count);
            Assert.Equal(2, fixture.Store.ListBackups().Count);
            Assert.All(fixture.Store.BackupsNotRekeyed, path =>
            {
                Assert.True(File.Exists(path));
                var copy = new EventStore();
                try
                {
                    copy.OpenReadOnly(path);
                    Assert.Equal("Kept", Assert.Single(copy.Events).Title);
                }
                finally { copy.Close(); }
            });
        }

        [Fact]
        public void FailedReopenAfterACommittedChangeStillCleansUp()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Kept"));
            string path = fixture.Store.FilePath;

            var outcome = fixture.Store.SetPassword("pw", File.Replace, File.Delete,
                (file, password) => throw new IOException("injected reopen failure"));

            Assert.True(outcome.ReopenNeeded);
            Assert.False(File.Exists(path + ".bak"));
            Assert.Null(outcome.RetainedBackup);
            Assert.Empty(fixture.Store.BackupsNotRekeyed);
            var backup = Assert.Single(EventStore.ListBackupsFor(path));
            Assert.True(EventStore.IsEncryptedFile(backup.Path));
            Assert.True(EventStore.IsEncryptedFile(path));

            fixture.Store.Open(path, "pw");
            Assert.Equal("Kept", Assert.Single(fixture.Store.Events).Title);
        }

        [Fact]
        public void ArchivingTakesTheCalendarsBackupsAlong()
        {
            string root = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
            string? savedDir = Settings.Get("KillendarDataDir");
            string? savedActive = Settings.Get("ActiveKillendar");
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                EventStore.SetDataDir(root);
                string work = Path.Combine(root, "Work.kcal");
                store.Open(work);
                store.Add(Appointment("Old"));
                Assert.NotNull(store.BackupNow(new DateTime(2026, 10, 21, 9, 30, 0)));
                store.Close();
                EventStore.SetActive("Work.kcal");

                string archived = EventStore.ArchiveActive();

                Assert.Empty(EventStore.ListBackupsFor(work));
                Assert.Single(EventStore.ListBackupsFor(archived));
            }
            finally
            {
                store.Close();
                Settings.Set("KillendarDataDir", savedDir ?? "");
                Settings.Set("ActiveKillendar", savedActive ?? "");
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task ImportCountsAsRunningWhileItWaitsForTheWriteGate()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Before"));
            var plan = fixture.Store.PlanImport([Appointment("Imported")]);
            bool seen;
            Task import;
            lock (fixture.Store.WriteGate)
            {
                import = Task.Run(() => fixture.Store.WriteImport(plan, CancellationToken.None));
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (!fixture.Store.IsImporting && wait.ElapsedMilliseconds < 2000) Thread.Sleep(10);
                seen = fixture.Store.IsImporting;
            }
            await import;

            Assert.True(seen);
            Assert.False(fixture.Store.IsImporting);
        }
    }
}
