using System.Globalization;
using System.Linq;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GJ-001: the day-of-week header labels must tell the seven days apart.</summary>
    public sealed class DateFlyoutLabelTests
    {
        private static string[] Initials(string culture)
        {
            var format = CultureInfo.GetCultureInfo(culture).DateTimeFormat;
            var flyout = typeof(App).Assembly.GetType("Killendar.Controls.DatePickerFlyout")!;
            return Enumerable.Range(0, 7)
                .Select(d => (string)KitTestHost.CallStatic(flyout, "DayInitial", format, (System.DayOfWeek)d)!)
                .ToArray();
        }

        [Fact]
        public void ChineseDayHeadersAreNotAllTheSameCharacter()
        {
            var labels = Initials("zh-CN");
            Assert.Equal(7, labels.Distinct().Count());
        }

        [Fact]
        public void EnglishKeepsTheSingleLetterHeaders()
        {
            Assert.Equal(new[] { "S", "M", "T", "W", "T", "F", "S" }, Initials("en-US"));
        }
    }
}
