using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    // SS-006: a crash between the two moves of a backup rekey leaves a .rekey-old copy under the
    // previous key. The next pass puts an orphan back where it belongs, so it is listed, pruned
    // and rewritten like any backup, and deletes one whose rewrite is already in place.
    [Collection("Calendar settings")]
    public sealed class RekeyLeftoverRegressionTests
    {
        [Fact]
        public void LeftoverRekeyCopiesAreRestoredOrRemoved()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Kept",
                Start = new DateTime(2026, 10, 22, 9, 0, 0), End = new DateTime(2026, 10, 22, 10, 0, 0) });
            string rewritten = fixture.Store.BackupNow(new DateTime(2026, 10, 20, 9, 0, 0))!;
            string orphaned = fixture.Store.BackupNow(new DateTime(2026, 10, 21, 9, 0, 0))!;
            // The rewrite of the first is in place and its old copy was left behind; the second
            // died after it was set aside and before its rewrite moved in.
            File.Copy(rewritten, rewritten + ".rekey-old");
            File.Move(orphaned, orphaned + ".rekey-old");

            fixture.Store.SetPassword("pw");

            Assert.False(File.Exists(rewritten + ".rekey-old"));
            Assert.False(File.Exists(orphaned + ".rekey-old"));
            Assert.Contains(fixture.Store.ListBackups(), b => string.Equals(b.Path, orphaned, StringComparison.OrdinalIgnoreCase));
            Assert.True(EventStore.IsEncryptedFile(orphaned));
            using var keyed = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = orphaned, Password = "pw", Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            keyed.Open();
            using var count = keyed.CreateCommand();
            count.CommandText = "SELECT count(*) FROM events";
            Assert.Equal(1L, count.ExecuteScalar());
        }

        [Fact]
        public void DeletingACalendarTakesItsRekeyLeftoversToo()
        {
            string root = Path.Combine(TestScratch.Root, "rekey-leftover", Guid.NewGuid().ToString("N"));
            string? savedDir = Settings.Get("KillendarDataDir"), savedActive = Settings.Get("ActiveKillendar");
            var store = new EventStore();
            try
            {
                Directory.CreateDirectory(root);
                EventStore.SetDataDir(root);
                store.Open(Path.Combine(root, "Work.kcal"));
                string backup = store.BackupNow(new DateTime(2026, 10, 21, 9, 0, 0))!;
                store.Close();
                File.Move(backup, backup + ".rekey-old");

                EventStore.DeleteKillendar("Work.kcal");

                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(backup)!));
            }
            finally
            {
                store.Close();
                Settings.Set("KillendarDataDir", savedDir ?? "");
                Settings.Set("ActiveKillendar", savedActive ?? "");
                Directory.Delete(root, true);
            }
        }
    }
}
