using System;
using System.IO;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // UX-001: typed but unsaved appointment input is never replaced without asking. Cancel, Esc
    // and closing the panel stay deliberate discards; only replacing the draft asks.
    [Collection("Calendar settings")]
    public sealed class DraftProtectionRegressionTests
    {
        [Fact]
        public void NewOverADirtyDraftAsksAndKeepEditingKeepsIt()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldTitle = "Half typed";
            fixture.View.DiscardAnswer = false;

            fixture.Editor.NewAt(new DateTime(2026, 10, 23, 9, 0, 0));

            Assert.Equal(1, fixture.View.DiscardQuestions);
            Assert.Equal("Half typed", fixture.View.FieldTitle);
        }

        [Fact]
        public void OpeningAnotherAppointmentOverADirtyDraftReplacesItOnlyAfterDiscard()
        {
            using var fixture = new AppointmentFixture();
            var other = new CalendarEvent { Title = "Other", Start = new DateTime(2026, 10, 24, 9, 0, 0),
                End = new DateTime(2026, 10, 24, 10, 0, 0) };
            fixture.Store.Add(other);
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldDescription = "Notes in progress";
            fixture.View.DiscardAnswer = true;

            fixture.Editor.Load(other);

            Assert.Equal(1, fixture.View.DiscardQuestions);
            Assert.Equal("Other", fixture.View.FieldTitle);
        }

        [Fact]
        public void UntouchedPanelIsReplacedWithoutAsking()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));

            fixture.Editor.NewAt(new DateTime(2026, 10, 23, 9, 0, 0));

            Assert.Equal(0, fixture.View.DiscardQuestions);
        }

        [Fact]
        public void LoadingAnAppointmentIsNotAnEdit()
        {
            using var fixture = new AppointmentFixture();
            var ev = new CalendarEvent { Title = "Loaded", Start = new DateTime(2026, 10, 24, 9, 0, 0),
                End = new DateTime(2026, 10, 24, 10, 0, 0), Repeat = RepeatFreq.Weekly, Attendees = ["a@example.com"] };
            fixture.Store.Add(ev);
            fixture.Editor.Load(ev);

            Assert.False(fixture.Editor.HasUnsavedChanges);
            fixture.Editor.NewAt(new DateTime(2026, 10, 23, 9, 0, 0));
            Assert.Equal(0, fixture.View.DiscardQuestions);
        }

        [Fact]
        public void ChangingOnlyTheRepeatCountsAsAnEdit()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldRepeat = RepeatFreq.Daily;

            Assert.True(fixture.Editor.HasUnsavedChanges);
        }

        [Fact]
        public void SavedDraftIsNoLongerDirty()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldTitle = "Saved";
            fixture.Editor.Save();
            Assert.Null(fixture.View.Error);

            fixture.Editor.NewAt(new DateTime(2026, 10, 23, 9, 0, 0));

            Assert.Equal(0, fixture.View.DiscardQuestions);
        }

        [Fact]
        public void FailedSaveKeepsTheDraftAndItsProtection()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldTitle = "Bad end";
            fixture.View.FieldEndDate = "not a date";
            fixture.Editor.Save();
            Assert.NotNull(fixture.View.Error);
            fixture.View.DiscardAnswer = false;

            fixture.Editor.NewAt(new DateTime(2026, 10, 23, 9, 0, 0));

            Assert.Equal("Bad end", fixture.View.FieldTitle);
            Assert.Equal(1, fixture.View.DiscardQuestions);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RenamedOrDeletedActiveKillendarIsNeverRecreatedEmpty(bool delete)
        {
            string root = Path.Combine(TestScratch.Root, "draft-rename", Guid.NewGuid().ToString("N"));
            string? savedDir = Settings.Get("KillendarDataDir"), savedActive = Settings.Get("ActiveKillendar");
            var store = new EventStore();
            try
            {
                Directory.CreateDirectory(root);
                EventStore.SetDataDir(root);
                foreach (string name in new[] { "Current.kcal", "Other.kcal" })
                {
                    store.Open(Path.Combine(root, name));
                    store.Close();
                }
                EventStore.SetActive("Current.kcal");
                EventStore.LastRename = null;
                // What Manage Killendars does: rename the active file, or delete it and move on.
                if (delete) { EventStore.DeleteKillendar("Current.kcal"); EventStore.SetActive("Other.kcal"); }
                else EventStore.RenameKillendar("Current.kcal", "Renamed.kcal");
                var host = new SecurityHostFake { LeaveDraft = false };

                new SecurityController(host, store).ApplyKillendarSelection(null, "Current.kcal", root,
                    Path.Combine(root, "Current.kcal"));

                Assert.False(File.Exists(Path.Combine(root, "Current.kcal")));
                Assert.Equal(0, host.DraftQuestions);
                Assert.Equal(Path.Combine(root, delete ? "Other.kcal" : "Renamed.kcal"), store.FilePath, ignoreCase: true);
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

        [Fact]
        public void SwitchingKillendarsWithADraftStaysPutWhenTheUserKeepsEditing()
        {
            string root = Path.Combine(TestScratch.Root, "draft-switch", Guid.NewGuid().ToString("N"));
            string? savedDir = Settings.Get("KillendarDataDir"), savedActive = Settings.Get("ActiveKillendar");
            var store = new EventStore();
            try
            {
                Directory.CreateDirectory(root);
                EventStore.SetDataDir(root);
                foreach (string name in new[] { "Current.kcal", "Other.kcal" })
                {
                    store.Open(Path.Combine(root, name));
                    store.Close();
                }
                EventStore.SetActive("Current.kcal");
                var host = new SecurityHostFake { LeaveDraft = false };

                new SecurityController(host, store).ApplyKillendarSelection("Other.kcal", "Current.kcal", root,
                    Path.Combine(root, "Current.kcal"));

                Assert.Equal(1, host.DraftQuestions);
                Assert.Equal(Path.Combine(root, "Current.kcal"), store.FilePath, ignoreCase: true);
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
