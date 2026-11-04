using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    // VE-012: Week and Day open at 07:00 the first time and come back where they were left.
    public sealed class TimeGridScrollMemoryTests
    {
        [Fact]
        public void FirstOpenStartsAtSevenInTheMorning()
            => Assert.Equal(7.0, TimeGridView.OpeningTopHour(null));

        [Fact]
        public void ReturningKeepsTheHourThatWasAtTheTop()
            => Assert.Equal(18.5, TimeGridView.OpeningTopHour(18.5));

        [Fact]
        public void ReturningToMidnightIsKeptRatherThanTreatedAsUnset()
            => Assert.Equal(0.0, TimeGridView.OpeningTopHour(0.0));
    }
}
