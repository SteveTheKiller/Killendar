using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S13: other Killendars shown beside the open one, read only, each in its own color.
    [Collection("Calendar settings")]
    public sealed class ShownCalendarsRegressionTests : IDisposable
    {
        private static readonly DateTime Day = new(2026, 10, 20);
        private readonly string _dir = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
        private readonly string? _oldDir = Settings.Get("KillendarDataDir");
        private readonly string? _oldShown = Settings.Get(ShownCalendars.Setting);

        public ShownCalendarsRegressionTests()
        {
            Directory.CreateDirectory(_dir);
            Settings.Set("KillendarDataDir", _dir);
            Settings.Set(ShownCalendars.Setting, "");
        }

        public void Dispose()
        {
            ShownCalendars.CloseAll();
            Settings.Set("KillendarDataDir", _oldDir ?? "");
            Settings.Set(ShownCalendars.Setting, _oldShown ?? "");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private string Calendar(string name, string title, string? password = null)
        {
            var store = new EventStore();
            store.Open(Path.Combine(_dir, name));
            store.Add(new CalendarEvent { Title = title, Start = Day.AddHours(9), End = Day.AddHours(10) });
            if (password != null) store.SetPassword(password);
            store.Close();
            return name;
        }

        [Fact]
        public void AnotherCalendarsAppointmentsShowMarkedWithItsNameAndColor()
        {
            Calendar("Work.kcal", "Standup");
            ShownCalendars.Toggle("Work.kcal", true, "Home.kcal");

            var rows = ShownCalendars.With([], Day, Day.AddDays(1));

            var row = Assert.Single(rows);
            Assert.Equal("Standup", row.Title);
            Assert.Equal("Work", row.SourceCalendar);
            Assert.Equal(ShownCalendars.Palette[0], row.SourceColor);
        }

        [Fact]
        public void TheOpenCalendarIsNeverShownTwice()
        {
            Calendar("Work.kcal", "Standup");
            ShownCalendars.Toggle("Work.kcal", true, "Work.kcal");

            Assert.Empty(ShownCalendars.With([], Day, Day.AddDays(1)));
        }

        [Fact]
        public void ALockedOrMissingCalendarDoesNotStopTheOthers()
        {
            Calendar("Work.kcal", "Standup");
            Calendar("Private.kcal", "Doctor", password: "secret");
            Settings.Set(ShownCalendars.Setting, "Work.kcal|#8E6CC9*Private.kcal|#D08A2E*Gone.kcal|#2E9C94");

            ShownCalendars.Reload("Home.kcal");

            Assert.Equal(ShownCalendars.State.Shown, ShownCalendars.Items.Single(i => i.Name == "Work.kcal").State);
            Assert.Equal(ShownCalendars.State.Locked, ShownCalendars.Items.Single(i => i.Name == "Private.kcal").State);
            Assert.Equal(ShownCalendars.State.Unavailable, ShownCalendars.Items.Single(i => i.Name == "Gone.kcal").State);
            Assert.Equal("Standup", Assert.Single(ShownCalendars.With([], Day, Day.AddDays(1))).Title);
        }

        [Fact]
        public void ALockedCalendarOpensWithItsOwnPasswordAndLocksWithTheApp()
        {
            Calendar("Private.kcal", "Doctor", password: "secret");
            ShownCalendars.Toggle("Private.kcal", true, "Home.kcal");
            var item = Assert.Single(ShownCalendars.Items);

            Assert.False(ShownCalendars.Unlock(item, "wrong"));
            Assert.True(ShownCalendars.Unlock(item, "secret"));
            Assert.Equal("Doctor", Assert.Single(ShownCalendars.With([], Day, Day.AddDays(1))).Title);

            ShownCalendars.CloseAll();
            ShownCalendars.Reload("Home.kcal");
            Assert.Equal(ShownCalendars.State.Locked, Assert.Single(ShownCalendars.Items).State);
        }

        [Fact]
        public void TheEditorWillNotOpenAnotherCalendarsAppointment()
        {
            using var fixture = new AppointmentFixture();
            var other = new CalendarEvent { Title = "Standup", Start = Day.AddHours(9), End = Day.AddHours(10), SourceCalendar = "Work", SourceColor = "#8E6CC9" };

            fixture.Editor.Load(other);

            Assert.Equal("Str_Status_OtherCalendar", fixture.View.LastStatus);
            Assert.False(fixture.Editor.IsEditingExisting);
        }

        [Fact]
        public void ShownCalendarsAreNeverWrittenTo()
        {
            string path = Path.Combine(_dir, Calendar("Work.kcal", "Standup"));
            var before = File.GetLastWriteTimeUtc(path);
            ShownCalendars.Toggle("Work.kcal", true, "Home.kcal");
            ShownCalendars.With([], Day, Day.AddDays(1));
            ShownCalendars.CloseAll();

            Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        }

        [Theory]
        [InlineData("A.kcal|#112233*a.kcal|#445566", 1)]
        [InlineData("A.kcal|red*B.kcal", 2)]
        [InlineData("**", 0)]
        public void TheRememberedListIsReadSafely(string setting, int count)
        {
            var parsed = ShownCalendars.Parse(setting);
            Assert.Equal(count, parsed.Count);
            Assert.All(parsed, p => Assert.Matches("^#[0-9A-Fa-f]{6}$", p.Color));
        }

        // KD-C review BF-023: a name with ';' in it is a legal Windows file name.
        [Fact]
        public void ANameWithASemicolonSurvivesTheList()
        {
            Calendar("a;b.kcal", "Standup");
            ShownCalendars.Toggle("a;b.kcal", true, "Home.kcal");

            Assert.Equal("a;b.kcal", Assert.Single(ShownCalendars.Parse(Settings.Get(ShownCalendars.Setting))).Name);
            Assert.Equal("Standup", Assert.Single(ShownCalendars.With([], Day, Day.AddDays(1))).Title);
        }

        // BF-010: a shown Killendar can be renamed and deleted while it is shown.
        [Fact]
        public void AShownCalendarCanBeRenamedAndDeleted()
        {
            Calendar("Work.kcal", "Standup");
            ShownCalendars.Toggle("Work.kcal", true, "Home.kcal");

            EventStore.RenameKillendar("Work.kcal", "Office.kcal");
            Assert.Equal("Office.kcal", Assert.Single(ShownCalendars.Parse(Settings.Get(ShownCalendars.Setting))).Name);
            ShownCalendars.Reload("Home.kcal");
            Assert.Equal(ShownCalendars.State.Shown, Assert.Single(ShownCalendars.Items).State);

            EventStore.DeleteKillendar("Office.kcal");
            Assert.Empty(ShownCalendars.Parse(Settings.Get(ShownCalendars.Setting)));
            Assert.Empty(ShownCalendars.Items);
        }

        // BF-007: with no password on the open Killendar, its locks still lock an encrypted one.
        [Fact]
        public void LockingEncryptedOnesLeavesTheRestShown()
        {
            Calendar("Work.kcal", "Standup");
            Calendar("Private.kcal", "Doctor", password: "secret");
            Settings.Set(ShownCalendars.Setting, "Work.kcal|#8E6CC9*Private.kcal|#D08A2E");
            ShownCalendars.Reload("Home.kcal");
            Assert.True(ShownCalendars.Unlock(ShownCalendars.Items.Single(i => i.Name == "Private.kcal"), "secret"));
            Assert.True(ShownCalendars.AnyUnlockedEncrypted);

            ShownCalendars.LockEncrypted();

            Assert.False(ShownCalendars.AnyUnlockedEncrypted);
            Assert.Equal(ShownCalendars.State.Locked, ShownCalendars.Items.Single(i => i.Name == "Private.kcal").State);
            Assert.Equal("Standup", Assert.Single(ShownCalendars.With([], Day, Day.AddDays(1))).Title);
        }
    }
}
