using System;
using System.IO;
using System.Windows.Input;
using Killendar.Controls;
using Killendar.Features;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    // Lock and Manage Killendars fixes found in review: rename chains, the locked dialog, a
    // forwarded .ics while locked, keyboard access to a locked window, and a password change
    // that committed but could not reopen. None of these shows a window.
    [Collection("Calendar settings")]
    public sealed class LockManageFixesRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, "lock-manage-fixes", Guid.NewGuid().ToString("N"));
        private readonly string? _savedDir = Settings.Get("KillendarDataDir");
        private readonly string? _savedActive = Settings.Get("ActiveKillendar");
        private readonly EventStore _store = new EventStore();
        private readonly SecurityHostFake _host = new SecurityHostFake();

        public LockManageFixesRegressionTests()
        {
            Directory.CreateDirectory(_root);
            EventStore.SetDataDir(_root);
            EventStore.LastRename = null;
        }

        public void Dispose()
        {
            _store.Close();
            EventStore.LastRename = null;
            Settings.Set("KillendarDataDir", _savedDir ?? "");
            Settings.Set("ActiveKillendar", _savedActive ?? "");
            Directory.Delete(_root, true);
        }

        private string Create(string name, string? password = null)
        {
            string path = Path.Combine(_root, name);
            _store.Open(path);
            if (password != null) _store.SetPassword(password);
            _store.Close();
            return path;
        }

        // ---- SF-009 ----

        [Fact]
        public void RenamingTheOpenKillendarTwiceKeepsItTheSameCalendar()
        {
            string a = Create("A.kcal");
            EventStore.SetActive("A.kcal");
            EventStore.RenameKillendar("A.kcal", "B.kcal");
            EventStore.RenameKillendar("B.kcal", "C.kcal");
            _host.LeaveDraft = false;

            new SecurityController(_host, _store).ApplyKillendarSelection(null, "A.kcal", _root, a);

            Assert.Equal(Path.Combine(_root, "C.kcal"), _store.FilePath, ignoreCase: true);
            Assert.Equal(0, _host.CloseSidebarCalls);
            Assert.Equal(0, _host.DraftQuestions);
            Assert.False(File.Exists(a));
        }

        [Fact]
        public void RenamingTheLockedKillendarTwiceStillRecognizesIt()
        {
            Create("A.kcal", "pw");
            EventStore.SetActive("A.kcal");
            _store.Open(Path.Combine(_root, "A.kcal"), "pw");
            var security = new SecurityController(_host, _store);
            security.RememberPassword("pw");
            Assert.True(security.Lock());

            security.ShowKillendars(() =>
            {
                EventStore.RenameKillendar("A.kcal", "B.kcal");
                EventStore.RenameKillendar("B.kcal", "C.kcal");
                return null;
            });

            Assert.True(security.IsLocked);
            Assert.Equal(0, _host.DraftQuestions);
            Assert.Equal("C.kcal", EventStore.ActiveFile, ignoreCase: true);
        }

        // ---- SF-003 ----

        [Theory]
        [InlineData(null, "A.kcal", true, true)]
        [InlineData("A.kcal", "A.kcal", false, false)]
        [InlineData("a.KCAL", "A.kcal", false, false)]
        [InlineData("A.kcal", "B.kcal", true, true)]
        public void LockedDialogOffersNoRenameOrDeleteForTheLockedFile(string? locked, string name, bool rename, bool delete)
        {
            var allowed = KillendarsDialog.AllowedActions(locked, name);

            Assert.Equal(rename, allowed.CanRename);
            Assert.Equal(delete, allowed.CanDelete);
        }

        // ---- SF-008 ----

        [Theory]
        [InlineData(@"C:\in\invite.ics", true, PendingOpenAction.Defer)]
        [InlineData(@"C:\in\work.kcal", true, PendingOpenAction.Defer)]
        [InlineData(@"C:\in\invite.ICS", false, PendingOpenAction.ImportIcs)]
        [InlineData(@"C:\in\work.kcal", false, PendingOpenAction.AdoptKcal)]
        [InlineData(null, true, PendingOpenAction.None)]
        [InlineData(null, false, PendingOpenAction.None)]
        public void AForwardedFileWaitsForTheUnlock(string? path, bool locked, PendingOpenAction expected)
            => Assert.Equal(expected, PendingOpenPolicy.Decide(path, locked));

        // ---- UI-005 ----

        [Theory]
        [InlineData(Key.Tab, Key.None, ModifierKeys.None, LockedKeyAction.PassThrough)]
        [InlineData(Key.Tab, Key.None, ModifierKeys.Shift, LockedKeyAction.PassThrough)]
        [InlineData(Key.Space, Key.None, ModifierKeys.None, LockedKeyAction.PassThrough)]
        [InlineData(Key.Enter, Key.None, ModifierKeys.None, LockedKeyAction.PassThrough)]
        [InlineData(Key.LeftShift, Key.None, ModifierKeys.Shift, LockedKeyAction.PassThrough)]
        [InlineData(Key.System, Key.F4, ModifierKeys.Alt, LockedKeyAction.PassThrough)]
        [InlineData(Key.System, Key.Space, ModifierKeys.Alt, LockedKeyAction.PassThrough)]
        [InlineData(Key.A, Key.None, ModifierKeys.None, LockedKeyAction.AskToUnlock)]
        [InlineData(Key.N, Key.None, ModifierKeys.Control, LockedKeyAction.AskToUnlock)]
        public void ADeclinedPromptLeavesTabbingAndTheWindowMenuAlone(Key key, Key systemKey, ModifierKeys mods, LockedKeyAction expected)
            => Assert.Equal(expected, LockedKeyPolicy.Decide(true, true, key, systemKey, mods));

        [Fact]
        public void AltF4AndAltSpaceAreNeverSwallowedEvenBeforeADecline()
        {
            Assert.Equal(LockedKeyAction.PassThrough, LockedKeyPolicy.Decide(true, false, Key.System, Key.F4, ModifierKeys.Alt));
            Assert.Equal(LockedKeyAction.PassThrough, LockedKeyPolicy.Decide(true, false, Key.System, Key.Space, ModifierKeys.Alt));
        }

        [Fact]
        public void TabStillAsksForThePasswordBeforeAnyDecline()
            => Assert.Equal(LockedKeyAction.AskToUnlock, LockedKeyPolicy.Decide(true, false, Key.Tab, Key.None, ModifierKeys.None));

        // ---- SF-006 follow-up ----

        [Fact]
        public void AChangeThatCommittedButCouldNotReopenIsReopenedWithTheNewPassword()
        {
            string path = Create("A.kcal", "old");
            EventStore.SetActive("A.kcal");
            _store.Open(path, "old");
            var security = new SecurityController(_host, _store);

            var outcome = _store.SetPassword("new", File.Replace, File.Delete,
                (_, _) => throw new IOException("injected reopen failure"));
            Assert.True(outcome.ReopenNeeded);
            Assert.False(_store.IsOpen);
            Assert.True(File.Exists(path + ".bak"));
            var settled = security.AfterPasswordChange(outcome, "new");

            Assert.True(_store.IsOpen);
            Assert.False(security.IsLocked);
            Assert.True(security.RemembersPassword);
            // SS-004: the reopen proved the rewritten file, so the kept previous copy goes.
            Assert.False(File.Exists(path + ".bak"));
            Assert.Null(settled.RetainedBackup);
        }

        [Fact]
        public void AChangeThatStaysClosedNamesThePreviousCopyItKept()
        {
            string path = Create("A.kcal", "old");
            EventStore.SetActive("A.kcal");
            var security = new SecurityController(_host, _store);

            security.AfterPasswordChange(new PasswordChangeOutcome(path + ".bak", true, reopenNeeded: true), "not the key");

            Assert.True(security.IsLocked);
            string status = _host.Statuses[_host.Statuses.Count - 1];
            Assert.Contains(path + ".bak", status);
            Assert.DoesNotContain("Str_Status_PwPlainBackupKept", status);
        }

        [Fact]
        public void AChangeThatStaysClosedLocksAndAsksForThePassword()
        {
            Create("A.kcal", "old");
            EventStore.SetActive("A.kcal");
            var security = new SecurityController(_host, _store);

            // Nothing can open the file with this password.
            security.AfterPasswordChange(new PasswordChangeOutcome(null, false, reopenNeeded: true), "not the key");

            Assert.False(_store.IsOpen);
            Assert.True(security.IsLocked);
            Assert.True(_host.Locked);
            Assert.False(security.RemembersPassword);
            Assert.Equal("Str_Pw_UnlockHead {0}", _host.Statuses[_host.Statuses.Count - 1]);
        }
    }
}
