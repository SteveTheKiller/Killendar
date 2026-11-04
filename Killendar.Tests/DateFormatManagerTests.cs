using System;
using System.Globalization;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class DateFormatManagerTests
    {
        [Fact]
        public void ExplicitEuropeanFormatDoesNotFollowWindowsPattern()
        {
            DateFormatManager.SetSetting = (_, _) => { };
            DateFormatManager.Apply(DateStyle.EU);

            Assert.Equal("31/12/2026", DateFormatManager.Format(new DateTime(2026, 12, 31)));
        }

        [Theory]
        [InlineData(DateStyle.US, "1/2/2026", "m/d/yyyy")]
        [InlineData(DateStyle.EU, "02/01/2026", "dd/mm/yyyy")]
        public void SlashStylesPrintALiteralSlashUnderAGermanCulture(DateStyle style, string expected, string hint)
        {
            var oldCurrent = CultureInfo.CurrentCulture;
            var oldDefault = CultureInfo.DefaultThreadCurrentCulture;
            try
            {
                var german = new CultureInfo("de-DE");
                CultureInfo.CurrentCulture = german;
                CultureInfo.DefaultThreadCurrentCulture = german;
                DateFormatManager.SetSetting = (_, _) => { };
                DateFormatManager.Apply(style);

                Assert.Equal(expected, DateFormatManager.Format(new DateTime(2026, 1, 2)));
                Assert.Equal(hint, DateFormatManager.Hint);
                if (style == DateStyle.US)
                {
                    Assert.True(DateFormatManager.TryParse("1/2/2026", out var parsed));
                    Assert.Equal(new DateTime(2026, 1, 2), parsed);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCurrent;
                CultureInfo.DefaultThreadCurrentCulture = oldDefault;
            }
        }

        // KD-J (IE-008): the fixed styles are Gregorian whatever the region's calendar.
        [Theory]
        [InlineData(DateStyle.Iso, "2026-11-03", "11-03")]
        [InlineData(DateStyle.US, "11/3/2026", "11/3")]
        [InlineData(DateStyle.EU, "03/11/2026", "03/11")]
        public void FixedStylesAreGregorianUnderAThaiCulture(DateStyle style, string expected, string monthDay)
        {
            var oldCurrent = CultureInfo.CurrentCulture;
            var oldStyle = DateFormatManager.Current;
            DateFormatManager.SetSetting = (_, _) => { };
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH");
                DateFormatManager.Apply(style);

                Assert.Equal(expected, DateFormatManager.Format(new DateTime(2026, 11, 3)));
                Assert.Equal(monthDay, DateFormatManager.MonthDay(new DateTime(2026, 11, 3)));
                Assert.True(DateFormatManager.TryParse(expected, out var typed));
                Assert.Equal(new DateTime(2026, 11, 3), typed);
                Assert.True(DateFormatManager.TryParse("2026-11-03", out var iso));
                Assert.Equal(new DateTime(2026, 11, 3), iso);
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCurrent;
                DateFormatManager.Apply(oldStyle);
            }
        }

        // The chosen style still wins when the date is typed with the region's separator: on a
        // German region the US style reads 1.2.2026 month first, as it did before IE-008.
        [Theory]
        [InlineData(DateStyle.US, "1.2.2026", 1, 2)]
        [InlineData(DateStyle.EU, "01.02.2026", 2, 1)]
        public void TheChosenStyleReadsTheRegionSeparator(DateStyle style, string typed, int month, int day)
        {
            var oldCurrent = CultureInfo.CurrentCulture;
            var oldStyle = DateFormatManager.Current;
            DateFormatManager.SetSetting = (_, _) => { };
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                DateFormatManager.Apply(style);

                Assert.True(DateFormatManager.TryParse(typed, out var date));
                Assert.Equal(new DateTime(2026, month, day), date);
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCurrent;
                DateFormatManager.Apply(oldStyle);
            }
        }

        [Fact]
        public void FollowWindowsKeepsTheRegionCalendar()
        {
            var oldCurrent = CultureInfo.CurrentCulture;
            var oldStyle = DateFormatManager.Current;
            DateFormatManager.SetSetting = (_, _) => { };
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH");
                DateFormatManager.Apply(DateStyle.FollowWindows);

                // 2026 is year 2569 of the Thai Buddhist era.
                Assert.Contains("2569", DateFormatManager.Format(new DateTime(2026, 11, 3)));
                Assert.True(DateFormatManager.TryParse(DateFormatManager.Format(new DateTime(2026, 11, 3)), out var typed));
                Assert.Equal(new DateTime(2026, 11, 3), typed);
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCurrent;
                DateFormatManager.Apply(oldStyle);
            }
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
