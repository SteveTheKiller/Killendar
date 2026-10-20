using System;
using System.IO;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class PasswordExportFailureRegressionTests
    {
        [Fact]
        public void FailedCipherExportDoesNotLeaveItsAttachedDatabaseBlockingRetry()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Surviving export failure",
                Start = new DateTime(2026, 10, 20, 9, 0, 0), End = new DateTime(2026, 10, 20, 10, 0, 0) });
            string path = fixture.Store.FilePath;
            using var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False");
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
                Assert.Throws<SqliteException>(() => fixture.Store.SetPassword("new password"));
            }
            finally { ChangeSchema(connection, schema); }
            connection.Close();

            Assert.True(fixture.Store.IsOpen);
            Assert.Equal("Surviving export failure", Assert.Single(fixture.Store.Events).Title);
            Assert.Null(Record.Exception(() => fixture.Store.SetPassword("new password")));
            fixture.Store.Close();
            fixture.Store.Open(path, "new password");
            Assert.True(fixture.Store.IsOpen);
            Assert.Equal("Surviving export failure", Assert.Single(fixture.Store.Events).Title);
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
