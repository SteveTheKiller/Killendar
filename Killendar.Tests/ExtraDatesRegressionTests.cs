using System;
using System.Collections.Generic;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-D: extra dates outside a series' pattern (RDATE).
    [Collection("Calendar settings")]
    public sealed class ExtraDatesRegressionTests
    {
        private static readonly DateTime First = new(2026, 10, 19, 9, 0, 0);   // a Monday

        private static CalendarEvent Weekly(params DateTime[] extras) => new()
        {
            Title = "Review", Start = First, End = First.AddHours(1), Repeat = RepeatFreq.Weekly,
            RepeatDays = [DayOfWeek.Monday], RepeatCount = 3, ExtraDates = [.. extras],
        };

        private static List<DateTime> Starts(CalendarEvent master) =>
            Recurrence.Expand(master, First.Date, First.Date.AddDays(90)).Select(o => o.Start).ToList();

        [Fact]
        public void ExtraDatesJoinThePatternAtItsTimeOfDay()
        {
            var starts = Starts(Weekly(new DateTime(2026, 10, 22), new DateTime(2026, 12, 24)));
            Assert.Equal([First, new DateTime(2026, 10, 22, 9, 0, 0), First.AddDays(7), First.AddDays(14),
                          new DateTime(2026, 12, 24, 9, 0, 0)], starts);   // past the count, as RFC 5545 has it
        }

        [Fact]
        public void ADateBothGiveIsShownOnce()
            => Assert.Equal(3, Starts(Weekly(First.AddDays(7).Date)).Count);

        [Fact]
        public void SkippingAnExtraDateSkipsIt()
        {
            var master = Weekly(new DateTime(2026, 10, 22));
            master.SkipDates = [new DateTime(2026, 10, 22)];
            Assert.Equal(3, Starts(master).Count);
        }

        [Fact]
        public void TheEditorSavesThemAndTheFileKeepsThem()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(First);
            fixture.View.FieldTitle = "Review";
            fixture.View.FieldRepeat = RepeatFreq.Weekly;
            fixture.View.FieldRepeatEveryN = 1;
            fixture.View.FieldRepeatDays = [DayOfWeek.Monday];
            fixture.View.FieldRepeatExtraDates = [new DateTime(2026, 10, 22)];
            fixture.Editor.Save();

            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);

            Assert.Equal([new DateTime(2026, 10, 22)], Assert.Single(fixture.Store.Events).ExtraDates);
        }

        [Fact]
        public void CalendarFilesCarryThemBothWays()
        {
            string text = IcsService.BuildIcs([Weekly(new DateTime(2026, 10, 22))]);
            Assert.Contains("RDATE:20261022T090000", text);

            var back = Assert.Single(IcsService.ParseText(text).Events);
            Assert.Equal([new DateTime(2026, 10, 22)], back.ExtraDates);
        }

        [Fact]
        public void ExtraDatesWithoutAPatternMakeASeries()
        {
            string ics = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:rdate@example.com",
                "DTSTART:20261019T090000", "DTEND:20261019T100000", "RDATE:20261021T090000,20261030T090000",
                "SUMMARY:Visits", "END:VEVENT", "END:VCALENDAR", "");
            var result = IcsService.ParseText(ics);
            var ev = Assert.Single(result.Events);
            Assert.Equal(0, result.Repeating);
            Assert.Equal([First, new DateTime(2026, 10, 21, 9, 0, 0), new DateTime(2026, 10, 30, 9, 0, 0)], Starts(ev));
        }

        [Fact]
        public void APeriodIsStillCounted()
        {
            string ics = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:period@example.com",
                "DTSTART:20261019T090000", "DTEND:20261019T100000", "RDATE;VALUE=PERIOD:20261021T090000/PT2H",
                "SUMMARY:Visits", "END:VEVENT", "END:VCALENDAR", "");
            Assert.Equal(1, IcsService.ParseText(ics).Repeating);
        }

        [Fact]
        public void SplittingDividesThem()
        {
            using var fixture = new AppointmentFixture();
            var master = Weekly(new DateTime(2026, 10, 22), new DateTime(2026, 11, 5));
            master.RepeatCount = 0;
            fixture.Store.Add(master);
            var third = fixture.Store.GetInRange(First.AddDays(14).Date, First.AddDays(15).Date).Single().Clone();
            third.Title = "Review, new room";

            fixture.Store.UpdateFollowing(third);

            var head = fixture.Store.Events.Single(e => e.Title == "Review");
            var tail = fixture.Store.Events.Single(e => e.Title == "Review, new room");
            Assert.Equal([new DateTime(2026, 10, 22)], head.ExtraDates);
            Assert.Equal([new DateTime(2026, 11, 5)], tail.ExtraDates);
        }
    }
}
