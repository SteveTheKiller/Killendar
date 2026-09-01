using System;
using System.Globalization;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class DateFormatManagerTests
    {
        [Fact]
        public void ExplicitEuropeanFormatDoesNotFollowWindowsPattern()
        {
            DateFormatManager.SetSetting = (_, _) => { };
            DateFormatManager.Apply(DateStyle.EU);

            Assert.Equal("31/12/2026", DateFormatManager.Format(new DateTime(2026, 12, 31)));
        }

        [Fact]
        public void ParserAcceptsIsoAlongsideSelectedFormat()
        {
            DateFormatManager.SetSetting = (_, _) => { };
            DateFormatManager.Apply(DateStyle.US);

            Assert.True(DateFormatManager.TryParse("2026-08-16", out var parsed));
            Assert.Equal(new DateTime(2026, 8, 16), parsed);
        }

        [Fact]
        public void LocaleCultureControlsGeneratedCalendarNames()
        {
            var english = LocaleManager.CultureFor(Locale.EnUS);
            var polish = LocaleManager.CultureFor(Locale.PlPL);
            var date = new DateTime(2026, 8, 17);

            Assert.Equal("Monday", date.ToString("dddd", english));
            Assert.StartsWith("ponied", date.ToString("dddd", polish));
            Assert.Equal(DayOfWeek.Monday, polish.DateTimeFormat.FirstDayOfWeek);
        }

        [Fact]
        public void WeekStartCanFollowWindowsIndependentlyFromLanguage()
        {
            WeekStartManager.GetSetting = _ => null;
            WeekStartManager.Initialize(DayOfWeek.Monday);

            Assert.Equal(WeekStartStyle.FollowWindows, WeekStartManager.Current);
            Assert.Equal(DayOfWeek.Monday, WeekStartManager.FirstDay);
        }

        [Fact]
        public void ExplicitWeekStartOverridesWindows()
        {
            WeekStartManager.GetSetting = _ => WeekStartStyle.Sunday.ToString();
            WeekStartManager.Initialize(DayOfWeek.Monday);

            Assert.Equal(DayOfWeek.Sunday, WeekStartManager.FirstDay);
        }
    }
}
