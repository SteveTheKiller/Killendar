using System;
using System.IO;
using Killendar.Features;
using Killendar.Features.About;
using Killendar.Models;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    // An unsaved appointment draft in the sidebar is never discarded without a prompt, and a
    // save that cannot land says so instead of looking like it worked.
    [Collection("Calendar settings")]
    public sealed class DraftLossRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, "draft-loss", Guid.NewGuid().ToString("N"));
        private readonly string? _savedDir = Settings.Get("KillendarDataDir");
        private readonly string? _savedActive = Settings.Get("ActiveKillendar");
        private readonly EventStore _store = new EventStore();
        private readonly SecurityHostFake _host = new SecurityHostFake();

        public DraftLossRegressionTests()
        {
            Directory.CreateDirectory(_root);
            EventStore.SetDataDir(_root);
            _store.Open(Path.Combine(_root, "Main.kcal"));
            EventStore.SetActive("Main.kcal");
        }

        public void Dispose()
        {
            _store.Close();
            Settings.Set("KillendarDataDir", _savedDir ?? "");
            Settings.Set("ActiveKillendar", _savedActive ?? "");
            Directory.Delete(_root, true);
        }

        [Fact]
        public void UpdatingAnAppointmentTheStoreDoesNotHoldReportsAFailure()
        {
            var ghost = new CalendarEvent { Title = "Ghost", Start = new DateTime(2026, 11, 3, 9, 0, 0),
                End = new DateTime(2026, 11, 3, 10, 0, 0) };

            _store.Update(ghost);

            Assert.NotNull(_store.LoadError);
            Assert.Empty(_store.Events);
        }

        [Fact]
        public void ManageKillendarsAsksAboutTheDraftBeforeTheStoreClosesAndKeepEditingChangesNothing()
        {
            var security = new SecurityController(_host, _store);
            _host.LeaveDraft = false;
            bool dialogShown = false;

            security.ShowKillendars(() => { dialogShown = true; return null; });

            Assert.Equal(1, _host.DraftQuestions);
            Assert.False(dialogShown);
            Assert.True(_store.IsOpen);
            Assert.Equal("Main.kcal", EventStore.ActiveFile, ignoreCase: true);
        }

        [Fact]
        public void ManageKillendarsAnsweredYesAsksOnceAndThenShowsTheDialogOnAClosedStore()
        {
            var security = new SecurityController(_host, _store);
            int questionsWhenShown = -1;
            bool openWhenShown = true;

            security.ShowKillendars(() =>
            {
                questionsWhenShown = _host.DraftQuestions;
                openWhenShown = _store.IsOpen;
                return null;
            });

            Assert.Equal(1, questionsWhenShown);
            Assert.False(openWhenShown);
            Assert.Equal(1, _host.DraftQuestions);
            Assert.True(_store.IsOpen);
        }

        [Fact]
        public void ReminderOpenKeepEditingDoesNotShowTheDayAndYesDoes()
        {
            int shown = 0;

            Assert.False(ReminderOpen.Run(() => false, () => shown++));
            Assert.Equal(0, shown);

            Assert.True(ReminderOpen.Run(() => true, () => shown++));
            Assert.Equal(1, shown);
        }

        private sealed class DraftGuardFake : IAboutDraftGuard
        {
            internal bool Answer;
            internal int Asked;
            public bool ConfirmDraftBeforeExit() { Asked++; return Answer; }
        }

        [Fact]
        public void UpdateNowShutdownIsCanceledWhenTheUserKeepsTheDraft()
        {
            var keep = new DraftGuardFake { Answer = false };
            var discard = new DraftGuardFake { Answer = true };

            Assert.False(AboutController.AllowsShutdown(keep));
            Assert.True(AboutController.AllowsShutdown(discard));
            Assert.Equal(1, keep.Asked);
            Assert.Equal(1, discard.Asked);
        }

        [Theory]
        [InlineData(false, false, ClosePrompt.ReplaceDraft)]
        [InlineData(true, false, ClosePrompt.DiscardDraft)]
        [InlineData(false, true, ClosePrompt.None)]
        [InlineData(true, true, ClosePrompt.None)]
        public void ClosePromptMatchesLockAndShutdown(bool isLocked, bool shutdownStarted, ClosePrompt expected)
        {
            Assert.Equal(expected, MainWindow.ClosePromptFor(isLocked, shutdownStarted));
        }

        [Fact]
        public void LockedCloseAsksTheDiscardQuestionNotTheSaveOne()
        {
            int replace = 0, discard = 0;

            bool allowed = MainWindow.ConfirmClose(true, false, () => { replace++; return true; }, () => { discard++; return false; });

            Assert.False(allowed);
            Assert.Equal(0, replace);
            Assert.Equal(1, discard);
        }
    }

    /// <summary>Escape closes whatever is on top. An open Reminder or Time zone dropdown is on top
    /// of the panel, so it gets the key and the draft survives.</summary>
    public sealed class EscapeTargetRegressionTests
    {
        [Fact]
        public void EscapeOnAnOpenDropdownIsLeftToTheDropdown()
            => Assert.Equal(EscapeAction.LeaveToDropDown, ShortcutKeyPolicy.Escape(true, false, false, true));

        [Fact]
        public void EscapeWithTheDropdownClosedClosesThePanel()
            => Assert.Equal(EscapeAction.CloseSidebar, ShortcutKeyPolicy.Escape(false, false, false, true));

        [Theory]
        [InlineData(true, false, EscapeAction.CloseShortcuts)]
        [InlineData(false, true, EscapeAction.CloseAbout)]
        public void AnOverlayStillClosesFirst(bool shortcuts, bool about, EscapeAction expected)
            => Assert.Equal(expected, ShortcutKeyPolicy.Escape(true, shortcuts, about, true));

        [Fact]
        public void EscapeWithNothingOpenDoesNothing()
            => Assert.Equal(EscapeAction.None, ShortcutKeyPolicy.Escape(false, false, false, false));
    }
}
