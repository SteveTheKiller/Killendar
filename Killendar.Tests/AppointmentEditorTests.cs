using System;
using System.Globalization;
using Killendar.Features;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class AppointmentEditorTests
    {
        // ---- time parsing (#14) ----

        [Theory]
        [InlineData("8", 8, 0)]
        [InlineData("8AM", 8, 0)]
        [InlineData("8 pm", 20, 0)]
        [InlineData("9:30", 9, 30)]
        [InlineData("9:30 pm", 21, 30)]
        [InlineData("21:30", 21, 30)]
        [InlineData("0930", 9, 30)]
        [InlineData("23", 23, 0)]
        public void ParsesWhatPeopleType(string raw, int hour, int minute)
        {
            using (new CultureScope("en-US"))
            {
                Assert.True(AppointmentEditor.TryParseTime(raw, out var t));
                Assert.Equal(new TimeSpan(hour, minute, 0), t);
            }
        }

        // A bare "8" and a half-typed "8A" fell through every pattern to a one-letter "H", which
        // the framework reads as a standard specifier and throws on. Every keystroke of a typed
        // time has to return, never throw: the parser runs on TextChanged.
        [Theory]
        [InlineData("8A")]
        [InlineData("8:")]
        [InlineData("x")]
        [InlineData("")]
        [InlineData("   ")]
        public void HalfTypedTimesReturnFalseInsteadOfThrowing(string raw)
        {
            using (new CultureScope("en-US"))
                Assert.False(AppointmentEditor.TryParseTime(raw, out _));
        }

        [Theory]
        [InlineData("pl-PL")]
        [InlineData("de-DE")]
        [InlineData("ja-JP")]
        public void HourAloneParsesUnderEveryShippedCulture(string culture)
        {
            using (new CultureScope(culture))
            {
                Assert.True(AppointmentEditor.TryParseTime("8", out var t));
                Assert.Equal(TimeSpan.FromHours(8), t);
                Assert.True(AppointmentEditor.TryParseTime("20:30", out var u));
                Assert.Equal(new TimeSpan(20, 30, 0), u);
                // Cultures without an AM/PM designator: whatever it returns, it must return.
                AppointmentEditor.TryParseTime("8A", out _);
                AppointmentEditor.TryParseTime("8AM", out _);
            }
        }

        // ---- the end follows the start (#14) ----

        private static void UseUsDates()
        {
            DateFormatManager.SetSetting = (_, _) => { };
            DateFormatManager.Apply(DateStyle.US);
        }

        private static string D(int y, int m, int d) => DateFormatManager.Format(new DateTime(y, m, d));

        [Fact]
        public void EarlierStartTimePullsTheEndTimeWithIt()
        {
            using (new CultureScope("en-US"))
            {
                UseUsDates();
                var was = new DateTime(2026, 9, 4, 9, 0, 0);
                var now = new DateTime(2026, 9, 4, 8, 0, 0);
                Assert.True(AppointmentEditor.TryFollowStart(was, now, false, D(2026, 9, 4), "10:00 AM",
                                                             out var endDate, out var endTime));
                Assert.Equal(D(2026, 9, 4), endDate);
                Assert.Equal("9:00 AM", endTime);
            }
        }

        [Fact]
        public void LaterStartDateMovesTheEndDateForward()
        {
            using (new CultureScope("en-US"))
            {
                UseUsDates();
                var was = new DateTime(2026, 9, 4, 9, 0, 0);
                var now = new DateTime(2026, 9, 6, 9, 0, 0);
                Assert.True(AppointmentEditor.TryFollowStart(was, now, false, D(2026, 9, 4), "10:00 AM",
                                                             out var endDate, out var endTime));
                Assert.Equal(D(2026, 9, 6), endDate);
                Assert.Equal("10:00 AM", endTime);
            }
        }

        [Fact]
        public void EndCrossesMidnightWhenTheShiftDemandsIt()
        {
            using (new CultureScope("en-US"))
            {
                UseUsDates();
                var was = new DateTime(2026, 9, 4, 9, 0, 0);
                var now = new DateTime(2026, 9, 4, 23, 30, 0);
                Assert.True(AppointmentEditor.TryFollowStart(was, now, false, D(2026, 9, 4), "10:00 AM",
                                                             out var endDate, out var endTime));
                Assert.Equal(D(2026, 9, 5), endDate);
                Assert.Equal("12:30 AM", endTime);
            }
        }

        [Fact]
        public void AllDayMovesWholeDaysAndLeavesTheHiddenTimeAlone()
        {
            using (new CultureScope("en-US"))
            {
                UseUsDates();
                var was = new DateTime(2026, 9, 4);
                var now = new DateTime(2026, 9, 6);
                Assert.True(AppointmentEditor.TryFollowStart(was, now, true, D(2026, 9, 5), "10:00 AM",
                                                             out var endDate, out var endTime));
                Assert.Equal(D(2026, 9, 7), endDate);
                Assert.Equal("10:00 AM", endTime);
            }
        }

        [Fact]
        public void UnreadableEndIsLeftAlone()
        {
            using (new CultureScope("en-US"))
            {
                UseUsDates();
                var was = new DateTime(2026, 9, 4, 9, 0, 0);
                var now = new DateTime(2026, 9, 4, 8, 0, 0);
                Assert.False(AppointmentEditor.TryFollowStart(was, now, false, D(2026, 9, 4), "10:",
                                                              out var endDate, out var endTime));
                Assert.Equal(D(2026, 9, 4), endDate);
                Assert.Equal("10:", endTime);
            }
        }

        /// <summary>The parser reads CultureInfo.CurrentCulture, as the app sets it per locale.</summary>
        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _was;
            public CultureScope(string name)
            {
                _was = CultureInfo.CurrentCulture;
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            }
            public void Dispose() => CultureInfo.CurrentCulture = _was;
        }
    }
}
