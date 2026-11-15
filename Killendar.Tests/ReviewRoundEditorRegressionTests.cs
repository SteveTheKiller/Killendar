using System;
using System.Linq;
using Killendar.Features;
using Killendar.Models;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class ReviewRoundEditorRegressionTests
    {
        [Fact]
        public void SaveAfterDraggingTheOpenAppointmentKeepsTheDraggedTime()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Call", Start = new DateTime(2026, 10, 22, 9, 0, 0),
                End = new DateTime(2026, 10, 22, 10, 0, 0) });
            var shown = fixture.Store.Events.Single();
            Assert.True(fixture.Editor.Load(shown));
            var calendar = new CalendarController(null!, fixture.Store, fixture.Editor);

            calendar.MoveDropped(shown, new DateTime(2026, 10, 23, 14, 0, 0));
            fixture.Editor.Save();

            var saved = fixture.Store.Events.Single();
            Assert.Equal(new DateTime(2026, 10, 23, 14, 0, 0), saved.Start);
            Assert.Equal(new DateTime(2026, 10, 23, 15, 0, 0), saved.End);
        }

        [Fact]
        public void DuplicatingOneDateOfASeriesMakesOneAppointmentOnThatDate()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Standup", Start = new DateTime(2026, 10, 19, 9, 0, 0),
                End = new DateTime(2026, 10, 19, 9, 30, 0), Repeat = RepeatFreq.Daily, RepeatCount = 5 });
            var master = fixture.Store.Events.Single();
            var third = fixture.Store.GetOnDay(new DateTime(2026, 10, 21)).Single();
            Assert.True(fixture.Editor.Load(third));

            fixture.Editor.Duplicate();
            fixture.Editor.Save();

            var copy = fixture.Store.Events.Single(e => e.Id != master.Id);
            Assert.Equal(RepeatFreq.None, copy.Repeat);
            Assert.Null(copy.SeriesId);
            Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0), copy.Start);
            Assert.Equal(new DateTime(2026, 10, 21, 9, 30, 0), copy.End);
        }
    }
}
