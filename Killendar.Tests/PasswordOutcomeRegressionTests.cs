using System;
using System.IO;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    // BF-009 and BF-010: password changes report what actually happened, and never leave an
    // unreported staged or backup copy of the calendar behind.
    [Collection("Calendar settings")]
    public sealed class PasswordOutcomeRegressionTests
    {
        [Fact]
        public void FailedPasswordRemovalRetiresItsStagedCopy()
        {
            using var fixture = EncryptedFixture();
            string path = fixture.Store.FilePath;

            WithBrokenSchema(path, "old password", () =>
                Assert.Throws<SqliteException>(() => fixture.Store.SetPassword(null)));

            Assert.False(File.Exists(path + ".rekey"));
            Assert.True(fixture.Store.IsOpen);
            Assert.True(fixture.Store.HasPassword);
        }

        [Fact]
        public void UndeletableStagedCopyIsReportedWithItsProtection()
        {
            using var fixture = EncryptedFixture();
            string path = fixture.Store.FilePath;

            var error = WithBrokenSchema(path, "old password", () =>
                Assert.Throws<IOException>(() => fixture.Store.SetPassword(null, File.Replace, target =>
                {
                    if (target.EndsWith(".rekey", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("injected cleanup failure");
                    File.Delete(target);
                })));

            Assert.Contains(path + ".rekey", error.Message);
            Assert.Contains("unencrypted", error.Message);
            Assert.IsType<SqliteException>(Assert.IsType<AggregateException>(error.InnerException).InnerExceptions[0]);
        }

        [Fact]
        public void CommittedPasswordWithUndeletableBackupReportsSuccessAndTheBackup()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment());
            string path = fixture.Store.FilePath;
            FileStream? holder = null;
            try
            {
                var outcome = fixture.Store.SetPassword("new password", (source, destination, backup) =>
                {
                    File.Replace(source, destination, backup);
                    holder = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read);
                });

                Assert.Equal(path + ".bak", outcome.RetainedBackup);
                Assert.True(outcome.RetainedBackupIsUnencrypted);
                Assert.True(fixture.Store.HasPassword);
            }
            finally { holder?.Dispose(); }

            fixture.Store.Close();
            fixture.Store.Open(path, "new password");
            Assert.Equal("Kept through cleanup failure", Assert.Single(fixture.Store.Events).Title);
        }

        [Fact]
        public void CleanPasswordChangeRetainsNothing()
        {
            using var fixture = EncryptedFixture();
            string path = fixture.Store.FilePath;

            var outcome = fixture.Store.SetPassword("newer password");

            Assert.Null(outcome.RetainedBackup);
            Assert.False(File.Exists(path + ".bak"));
            Assert.False(File.Exists(path + ".rekey"));
        }

        [Theory]
        [InlineData(null, false, "Str_Status_PwChanged {0}")]
        [InlineData(@"C:\cal\work.kcal.bak", false, @"Str_Status_PwBackupKept C:\cal\work.kcal.bak")]
        [InlineData(@"C:\cal\work.kcal.bak", true, @"Str_Status_PwPlainBackupKept C:\cal\work.kcal.bak")]
        public void StatusNamesAnyRetainedBackup(string? backup, bool unencrypted, string expected)
        {
            var security = new SecurityController(new SecurityHostFake(), new EventStore());

            Assert.Equal(expected, security.PasswordStatus(
                new PasswordChangeOutcome(backup, unencrypted), "Str_Status_PwChanged"));
        }

        private static AppointmentFixture EncryptedFixture()
        {
            var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment());
            fixture.Store.SetPassword("old password");
            return fixture;
        }

        private static CalendarEvent Appointment() => new CalendarEvent
        {
            Title = "Kept through cleanup failure",
            Start = new DateTime(2026, 10, 20, 9, 0, 0),
            End = new DateTime(2026, 10, 20, 10, 0, 0)
        };

        private static T WithBrokenSchema<T>(string path, string password, Func<T> act)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Password = password, Pooling = false }.ToString());
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
                return act();
            }
            finally { ChangeSchema(connection, schema); }
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
