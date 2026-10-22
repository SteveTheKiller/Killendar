using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S14: "the second Tuesday" and "the last Friday" of each month.
    [Collection("Calendar settings")]
    public sealed class MonthlyWeekdayRegressionTests
    {
        private static CalendarEvent Monthly(DateTime start, int week) => new()
        {
            Title = "Board", Start = start, End = start.AddHours(1),
            Repeat = RepeatFreq.Monthly, RepeatMonthWeek = week,
        };

        [Fact]
        public void SecondTuesdayEachMonth()
        {
            var ev = Monthly(new DateTime(2026, 10, 13, 9, 0, 0), 2);   // 13 Oct 2026 is the 2nd Tuesday
            var dates = Recurrence.Expand(ev, new DateTime(2026, 10, 1), new DateTime(2027, 2, 1)).Select(o => o.Start).ToList();
            Assert.Equal([new DateTime(2026, 10, 13, 9, 0, 0), new DateTime(2026, 11, 10, 9, 0, 0),
                          new DateTime(2026, 12, 8, 9, 0, 0), new DateTime(2027, 1, 12, 9, 0, 0)], dates);
        }

        [Fact]
        public void LastFridayEachMonthAndCountsEveryMonth()
        {
            var ev = Monthly(new DateTime(2026, 10, 30, 16, 0, 0), -1);
            ev.RepeatCount = 3;
            var dates = Recurrence.Expand(ev, new DateTime(2026, 10, 1), new DateTime(2027, 6, 1)).Select(o => o.Start.Date).ToList();
            Assert.Equal([new DateTime(2026, 10, 30), new DateTime(2026, 11, 27), new DateTime(2026, 12, 25)], dates);
        }

        [Fact]
        public void GeneratesDateFollowsTheRule()
        {
            var ev = Monthly(new DateTime(2026, 10, 13, 9, 0, 0), 2);
            Assert.True(Recurrence.GeneratesDate(ev, new DateTime(2026, 11, 10)));
            Assert.False(Recurrence.GeneratesDate(ev, new DateTime(2026, 11, 13)));
        }

        // 27 Oct 2026 is the fourth and also the last Tuesday of the month.
        [Theory]
        [InlineData("RRULE:FREQ=MONTHLY;BYDAY=4TU", 4)]
        [InlineData("RRULE:FREQ=MONTHLY;BYDAY=-1TU", -1)]
        [InlineData("RRULE:FREQ=MONTHLY;BYDAY=TU;BYSETPOS=4", 4)]
        [InlineData("RRULE:FREQ=MONTHLY;BYSETPOS=-1;BYDAY=TU", -1)]
        public void ImportsOrdinalMonthlyRules(string rule, int week)
        {
            string ics = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:ord@example.com",
                "DTSTART:20261027T090000", "DTEND:20261027T100000", "SUMMARY:Ordinal", rule, "END:VEVENT", "END:VCALENDAR", "");
            var ev = Assert.Single(IcsService.ParseText(ics).Events);
            Assert.Equal(RepeatFreq.Monthly, ev.Repeat);
            Assert.Equal(week, ev.RepeatMonthWeek);
        }

        [Fact]
        public void RuleOnAnotherWeekdayThanTheStartIsRefused()
        {
            string ics = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:mismatch@example.com",
                "DTSTART:20261027T090000", "DTEND:20261027T100000", "SUMMARY:Mismatch", "RRULE:FREQ=MONTHLY;BYDAY=2MO",
                "END:VEVENT", "END:VCALENDAR", "");
            var ev = Assert.Single(IcsService.ParseText(ics).Events);
            Assert.Equal(RepeatFreq.None, ev.Repeat);   // imported once and counted, as before
        }

        [Theory]
        [InlineData(2, "BYDAY=2TU")]
        [InlineData(-1, "BYDAY=-1TU")]
        public void ExportsTheOrdinalRule(int week, string expected)
        {
            var ev = Monthly(new DateTime(2026, 10, 13, 9, 0, 0), week);
            Assert.Contains(expected, IcsService.BuildIcs([ev]));
        }

        [Fact]
        public void StoredAndReadBack()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Monthly(new DateTime(2026, 10, 13, 9, 0, 0), 2));
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);
            Assert.Equal(2, Assert.Single(fixture.Store.Events).RepeatMonthWeek);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(13, 2)]
        [InlineData(27, 4)]
        [InlineData(31, 5)]
        public void WeekOfMonthFromTheDate(int day, int week)
            => Assert.Equal(week, Recurrence.WeekOfMonth(new DateTime(2026, 10, day)));

        [Fact]
        public void EditorSavesTheMonthlyChoice()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 13, 9, 0, 0));
            fixture.View.FieldTitle = "Board";
            fixture.View.FieldRepeat = RepeatFreq.Monthly;
            fixture.View.FieldRepeatEveryN = 1;
            fixture.View.FieldRepeatMonthWeek = 2;

            fixture.Editor.Save();

            Assert.Equal(2, Assert.Single(fixture.Store.Events).RepeatMonthWeek);
        }

        // KD-C review BF-002: a start moved to another week takes its weekday rule with it.
        [Fact]
        public void MovingTheSeriesStartFollowsTheNewWeek()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Monthly(new DateTime(2026, 10, 13, 9, 0, 0), 2));
            var moved = fixture.Store.Events[0].Clone();
            moved.Start = new DateTime(2026, 10, 21, 9, 0, 0);   // the third Wednesday
            moved.End = moved.Start.AddHours(1);

            fixture.Store.UpdateSeries(moved);

            var saved = Assert.Single(fixture.Store.Events);
            Assert.Equal(3, saved.RepeatMonthWeek);
            Assert.Contains(fixture.Store.GetInRange(new DateTime(2026, 10, 1), new DateTime(2026, 11, 1)),
                o => o.Start == new DateTime(2026, 10, 21, 9, 0, 0));
        }

        [Fact]
        public void AStaleChoiceFromThePanelIsFittedToTheStart()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 19, 9, 0, 0));   // the third Monday
            fixture.View.FieldTitle = "Board";
            fixture.View.FieldRepeat = RepeatFreq.Monthly;
            fixture.View.FieldRepeatEveryN = 1;
            fixture.View.FieldRepeatMonthWeek = 2;

            fixture.Editor.Save();

            Assert.Equal(3, Assert.Single(fixture.Store.Events).RepeatMonthWeek);
        }

        // BF-006: RFC 5545 lets RRULE come before DTSTART.
        [Fact]
        public void ARuleBeforeItsStartStillImports()
        {
            string ics = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:order@example.com",
                "RRULE:FREQ=MONTHLY;BYDAY=4TU", "DTSTART:20261027T090000", "DTEND:20261027T100000", "SUMMARY:Order",
                "END:VEVENT", "END:VCALENDAR", "");
            var ev = Assert.Single(IcsService.ParseText(ics).Events);
            Assert.Equal(RepeatFreq.Monthly, ev.Repeat);
            Assert.Equal(4, ev.RepeatMonthWeek);
        }

        // BF-003: a series kept on another zone's clock is checked and written on that clock.
        [Fact]
        public void AZonedSeriesUsesItsOwnClocksWeekday()
        {
            var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            var clock = new DateTime(2026, 10, 13, 8, 0, 0);   // the second Tuesday, 8:00 in Tokyo
            var ev = Monthly(TimeZones.FromZone(clock, tokyo), 2);
            ev.End = ev.Start.AddHours(1);
            TimeZones.Apply(ev, tokyo.Id);
            Assert.Equal(clock, ev.RepeatAnchor);

            string text = IcsService.BuildIcs([ev]);

            Assert.Contains("BYDAY=2TU", text);
            var back = Assert.Single(IcsService.ParseText(text).Events);
            Assert.Equal(RepeatFreq.Monthly, back.Repeat);
            Assert.Equal(2, back.RepeatMonthWeek);
        }
    }
}
