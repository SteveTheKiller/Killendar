using System;
using Killendar.Models;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class RepeatIntervalValidationTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void InvalidIntervalRefusesSaveAndKeepsTheEditorOpen(int interval)
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 11, 12, 9, 0, 0));
            fixture.View.FieldTitle = "Invalid interval";
            fixture.View.FieldRepeat = RepeatFreq.Daily;
            fixture.View.FieldRepeatEveryN = interval;

            fixture.Editor.Save();

            Assert.NotNull(fixture.View.Error);
            Assert.False(fixture.View.Closed);
            Assert.Empty(fixture.Store.Events);
        }
    }
}
