using Killendar.Models;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    // UX-005: the repeat interval box is named for what it asks, with the unit the pattern uses.
    public sealed class RepeatIntervalNameRegressionTests
    {
        [Theory]
        [InlineData(RepeatFreq.Daily, "Repeat interval (days)")]
        [InlineData(RepeatFreq.Weekly, "Repeat interval (weeks)")]
        [InlineData(RepeatFreq.Monthly, "Repeat interval (months)")]
        [InlineData(RepeatFreq.Yearly, "Repeat interval (years)")]
        public void NameIncludesTheCurrentUnit(RepeatFreq frequency, string expected)
        {
            var english = new EnglishCalendarHost();
            Assert.Equal(expected, MainWindow.IntervalAccessibleName(frequency, english.Loc));
        }
    }
}
