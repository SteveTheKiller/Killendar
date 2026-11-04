using System;
using System.IO;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    // SS-005: only a real key failure reads as a wrong password. A plaintext calendar that another
    // program holds open is not taken for an encrypted one, and its error is shown as it is.
    [Collection("Calendar settings")]
    public sealed class UnlockErrorRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, "unlock-errors", Guid.NewGuid().ToString("N"));
        private readonly EventStore _store = new EventStore();

        public UnlockErrorRegressionTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            _store.Close();
            Directory.Delete(_root, true);
        }

        private string Create(string name, string? password = null)
        {
            string path = Path.Combine(_root, name);
            _store.Open(path);
            _store.Add(new CalendarEvent { Title = "Kept",
                Start = new DateTime(2026, 10, 20, 9, 0, 0), End = new DateTime(2026, 10, 20, 10, 0, 0) });
            if (password != null) _store.SetPassword(password);
            _store.Close();
            return path;
        }

        [Fact]
        public void PlaintextFileHeldByAnotherProgramIsNotTakenForEncrypted()
        {
            string path = Create("Plain.kcal");
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.False(EventStore.IsEncryptedFile(path));
            Assert.False(EventStore.IsEncryptedFile(path));
        }

        [Fact]
        public void EncryptedFileIsStillTakenForEncrypted()
            => Assert.True(EventStore.IsEncryptedFile(Create("Secret.kcal", "pw")));

        [Fact]
        public void WrongPasswordAsksForThePasswordAgain()
        {
            string path = Create("Secret.kcal", "pw");
            var error = Assert.Throws<SqliteException>(() => _store.Open(path, "not it"));

            var heading = new SecurityController(new SecurityHostFake(), _store).UnlockFailureHeading(error);

            Assert.Equal("Str_Pw_WrongPw {0}", heading);
        }

        [Fact]
        public void FileHeldByAnotherProgramReportsItsOwnError()
        {
            string path = Create("Secret.kcal", "pw");
            SqliteException error;
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                error = Assert.Throws<SqliteException>(() => _store.Open(path, "pw"));

            var heading = new SecurityController(new SecurityHostFake(), _store).UnlockFailureHeading(error);

            Assert.Equal("Str_Status_LoadError " + error.Message, heading);
        }
    }
}
