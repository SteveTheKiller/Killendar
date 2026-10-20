using System;
using System.IO;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class PasswordRecoveryTests
    {
        [Theory]
        [InlineData(null, "new key")]
        [InlineData("old key", "new key")]
        [InlineData("old key", null)]
        public void PasswordChangeRecoversWhenReplaceMovesTheOriginalThenFails(string? oldKey, string? newKey)
        {
            using var fixture = new CalendarFixture(oldKey);
            int replacements = 0;
            Exception? error = Record.Exception(() => fixture.Store.SetPassword(newKey, (source, destination, backup) =>
            {
                if (++replacements == 1)
                {
                    File.Move(destination, backup);
                    throw new IOException("Replacement failed after renaming the original.", unchecked((int)0x80070499));
                }
                File.Replace(source, destination, backup);
            }));
            Assert.True(fixture.Store.IsOpen);
            Assert.Equal("Surviving event", Assert.Single(fixture.Store.Events).Title);
            Assert.Null(error);
            Assert.Equal(2, replacements);
            fixture.Store.Close();
            Assert.Throws<SqliteException>(() => fixture.Store.Open(fixture.Path, "incorrect key"));
            fixture.Store.Open(fixture.Path, newKey);
            Assert.Equal("Surviving event", Assert.Single(fixture.Store.Events).Title);
            Assert.False(File.Exists(fixture.Path + ".bak"));
            Assert.False(File.Exists(fixture.Path + ".rekey"));
        }

        [Fact]
        public void FailedRestorationKeepsBothCompleteDatabasesWithoutCreatingAnEmptyOne()
        {
            using var fixture = new CalendarFixture("old key");
            FileStream? heldBackup = null;
            try
            {
                Assert.Throws<IOException>(() => fixture.Store.SetPassword("new key", (source, destination, backup) =>
                {
                    File.Move(destination, backup);
                    heldBackup = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.None);
                    throw new IOException("Replacement failed after renaming the original.", unchecked((int)0x80070499));
                }));
                Assert.False(fixture.Store.IsOpen);
                Assert.False(File.Exists(fixture.Path));
                Assert.True(File.Exists(fixture.Path + ".bak"));
                Assert.True(File.Exists(fixture.Path + ".rekey"));
            }
            finally { heldBackup?.Dispose(); }
            foreach (var copy in new[] { (Path: fixture.Path + ".bak", Key: "old key"), (Path: fixture.Path + ".rekey", Key: "new key") })
            {
                using var database = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = copy.Path, Password = copy.Key, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
                database.Open();
                using var query = database.CreateCommand();
                query.CommandText = "SELECT title FROM events";
                Assert.Equal("Surviving event", query.ExecuteScalar());
            }
            File.Copy(fixture.Path + ".bak", fixture.Path);
            fixture.Store.Open(fixture.Path, "old key");
            Assert.Equal("Surviving event", Assert.Single(fixture.Store.Events).Title);
        }

        [Fact]
        public void SharingFailuresStillTryEightReplacementsBeforeTheMoveFallback()
        {
            using var fixture = new CalendarFixture("old key");
            int replacements = 0;
            fixture.Store.SetPassword("new key", (source, destination, backup) =>
            {
                replacements++;
                throw new IOException("Synthetic sharing failure.", unchecked((int)0x80070020));
            });
            Assert.Equal(8, replacements);
            fixture.Store.Close();
            Assert.Throws<SqliteException>(() => fixture.Store.Open(fixture.Path, "old key"));
            fixture.Store.Open(fixture.Path, "new key");
            Assert.Equal("Surviving event", Assert.Single(fixture.Store.Events).Title);
        }

        private sealed class CalendarFixture : IDisposable
        {
            private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KillendarPasswordTests", Guid.NewGuid().ToString("N"));
            public EventStore Store { get; } = new EventStore();
            public string Path => System.IO.Path.Combine(_directory, "test.kcal");
            public CalendarFixture(string? password)
            {
                Directory.CreateDirectory(_directory);
                Store.Open(Path, password);
                Store.Add(new CalendarEvent { Title = "Surviving event", Start = new DateTime(2026, 10, 20, 9, 0, 0), End = new DateTime(2026, 10, 20, 10, 0, 0) });
            }
            public void Dispose() { Store.Close(); Directory.Delete(_directory, true); }
        }
    }
}
