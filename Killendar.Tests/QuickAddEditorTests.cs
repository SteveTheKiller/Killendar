using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Sentence box: Enter opens the edit panel filled in, and nothing is saved until Save.
    [Collection("Calendar settings")]
    public sealed class QuickAddEditorTests
    {
        [Fact]
        public void ATimedLineOpensThePanelFilledInAndSavesOnlyOnSave()
        {
            using var fixture = new AppointmentFixture();
            var line = new QuickAddResult
            {
                Title = "Lunch with Dave", Location = "Mel's", Categories = "food",
                Start = new DateTime(2026, 11, 20, 12, 30, 0), End = new DateTime(2026, 11, 20, 14, 0, 0),
            };

            Assert.True(fixture.Editor.NewFromQuickAdd(line));
            Assert.Empty(fixture.Store.Events);
            Assert.Equal("Lunch with Dave", fixture.View.FieldTitle);

            fixture.Editor.Save();

            var made = Assert.Single(fixture.Store.Events);
            Assert.Equal("Lunch with Dave", made.Title);
            Assert.Equal("Mel's", made.Location);
            Assert.Equal(new DateTime(2026, 11, 20, 12, 30, 0), made.Start);
            Assert.Equal(new DateTime(2026, 11, 20, 14, 0, 0), made.End);
            Assert.False(made.AllDay);
            Assert.Contains("food", made.Categories);
        }

        [Fact]
        public void AnAllDayLineOpensAnAllDayAppointment()
        {
            using var fixture = new AppointmentFixture();
            var line = new QuickAddResult
            {
                Title = "Travel", AllDay = true,
                Start = new DateTime(2026, 11, 21), End = new DateTime(2026, 11, 21),
            };

            Assert.True(fixture.Editor.NewFromQuickAdd(line));
            fixture.Editor.Save();

            var made = Assert.Single(fixture.Store.Events);
            Assert.True(made.AllDay);
            Assert.Equal(new DateTime(2026, 11, 21), made.Start.Date);
            Assert.Equal(new DateTime(2026, 11, 22), made.End.Date);
        }
    }
}
