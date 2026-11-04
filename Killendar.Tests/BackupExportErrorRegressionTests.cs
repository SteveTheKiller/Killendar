using System;
using System.IO;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    // SS-003: when the export inside a backup fails, LastBackupError names that failure, not
    // the COMMIT or DETACH that ran after it.
    [Collection("Calendar settings")]
    public sealed class BackupExportErrorRegressionTests
    {
        [Fact]
        public void ExportThatFailsWritingReportsTheWriteFailure()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Kept",
                Start = new DateTime(2026, 10, 20, 9, 0, 0), End = new DateTime(2026, 10, 20, 10, 0, 0) });
            using (var connection = new SqliteConnection("Data Source=" + fixture.Store.FilePath + ";Pooling=False"))
            {
                // Big enough that the export spills pages to the backup file before COMMIT.
                connection.Open();
                using var pad = connection.CreateCommand();
                pad.CommandText = "CREATE TABLE padding(data BLOB); INSERT INTO padding VALUES(zeroblob(24000000));";
                pad.ExecuteNonQuery();
            }
            string dir = Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, "Backups");
            Directory.CreateDirectory(dir);
            string partial = Path.Combine(dir,
                Path.GetFileNameWithoutExtension(fixture.Store.FilePath) + " 2026-10-21 090000.kcal.partial");
            // Held without delete sharing, so the export opens this very file, and locked past the
            // first megabyte, so the spilled pages fail to write: an I/O error inside the transaction.
            using (var held = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                held.Lock(1L << 20, 1L << 29);
                Assert.Null(fixture.Store.BackupNow(new DateTime(2026, 10, 21, 9, 0, 0)));
                held.Unlock(1L << 20, 1L << 29);
            }

            string error = fixture.Store.LastBackupError ?? "";
            Assert.DoesNotContain("no transaction", error);
            Assert.DoesNotContain("locked", error);
            Assert.Contains("I/O", error);
        }

        [Fact]
        public void FailedExportReportsItsOwnCause()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Kept",
                Start = new DateTime(2026, 10, 20, 9, 0, 0), End = new DateTime(2026, 10, 20, 10, 0, 0) });
            using var connection = new SqliteConnection("Data Source=" + fixture.Store.FilePath + ";Pooling=False");
            connection.Open();
            string schema;
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT sql FROM sqlite_master WHERE name='events'";
                schema = (string)read.ExecuteScalar()!;
            }
            try
            {
                ChangeSchema(connection, "CREATE TABLE events (broken syntax HERE)");
                Assert.Null(fixture.Store.BackupNow(new DateTime(2026, 10, 21, 9, 0, 0)));
            }
            finally { ChangeSchema(connection, schema); }

            string error = fixture.Store.LastBackupError ?? "";
            Assert.DoesNotContain("no transaction", error);
            Assert.DoesNotContain("locked", error);
            Assert.Contains("no such column", error);
        }

        private static void ChangeSchema(SqliteConnection connection, string schema)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA writable_schema=ON; UPDATE sqlite_master SET sql=$sql WHERE name='events'; PRAGMA writable_schema=OFF;";
            command.Parameters.AddWithValue("$sql", schema);
            command.ExecuteNonQuery();
        }
    }
}
