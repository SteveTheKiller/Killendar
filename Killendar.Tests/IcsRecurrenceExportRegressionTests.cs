using System;
using System.Linq;
using System.Globalization;
using System.Text;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public class IcsRecurrenceExportRegressionTests
    {
        [Fact]
        public void NativeMondayEveningWeeklySeriesRetainsItsCalendarDayAcrossExport()
        {
            Assert.Equal("Pacific Standard Time", TimeZoneInfo.Local.Id);
            var original = new CalendarEvent
            {
                Start = new DateTime(2026, 10, 19, 20, 0, 0, DateTimeKind.Local),
                End = new DateTime(2026, 10, 19, 21, 0, 0, DateTimeKind.Local),
                Repeat = RepeatFreq.Weekly,
                RepeatDays = { DayOfWeek.Monday },
                RepeatCount = 4
            };
            var exported = IcsService.BuildIcs(new[] { original });
            var restored = Assert.Single(IcsService.ParseText(exported).Events);
            var expected = new[] { new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 27, 3, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 3, 4, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc) };
            Assert.Equal(expected, Recurrence.Expand(restored, new DateTime(2026, 10, 18), new DateTime(2026, 11, 12)).Select(e => e.Start.ToUniversalTime()).ToArray());
        }

        [Fact]
        public void SourceZoneSeriesIncludesDefinitionAndRetainsSourceClock()
        {
            const string input = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:source-zone-export\r\nDTSTART;TZID=Europe/London:20261024T090000\r\nDTEND;TZID=Europe/London:20261024T100000\r\nRRULE:FREQ=DAILY;COUNT=10\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var original = Assert.Single(IcsService.ParseText(input).Events);
            string exported = IcsService.BuildIcs(new[] { original });
            Assert.Contains("BEGIN:VTIMEZONE", exported);
            var restored = Assert.Single(IcsService.ParseText(exported).Events);
            Assert.Equal(RecurrenceTimeBasis.SourceZone, restored.RepeatTimeBasis);
            Assert.Equal("Europe/London", restored.RepeatZoneId);
            Assert.Equal(new DateTime(2026, 10, 24, 9, 0, 0), restored.RepeatAnchor);
            var dates = Recurrence.Expand(restored, new DateTime(2026, 10, 23), new DateTime(2026, 11, 5)).ToArray();
            Assert.Equal(10, dates.Length);
            for (int n = 0; n < dates.Length; n++)
                Assert.Equal(new DateTime(2026, 10, 24, n == 0 ? 8 : 9, 0, 0, DateTimeKind.Utc).AddDays(n), dates[n].Start.ToUniversalTime());
        }

        [Theory]
        [InlineData(RepeatFreq.Monthly, 2026, 10, 20, 23, "20261021T063000Z,20261121T073000Z,20261221T073000Z")]
        [InlineData(RepeatFreq.Yearly, 2026, 12, 31, 20, "20270101T043000Z,20280101T043000Z,20290101T043000Z")]
        [InlineData(RepeatFreq.Daily, 2026, 10, 31, 20, "20261101T033000Z,20261102T043000Z,20261103T043000Z")]
        public void NativeCalendarAnchorsStayFloatingAcrossUtcRolloverAndDst(RepeatFreq repeat, int year, int month, int day, int hour, string expected)
        {
            var start = new DateTime(year, month, day, hour, 30, 0, DateTimeKind.Local);
            var original = new CalendarEvent { Start = start, End = start.AddHours(1), Repeat = repeat, RepeatCount = 3 };
            string exported = IcsService.BuildIcs(new[] { original });
            Assert.DoesNotContain("BEGIN:VTIMEZONE", exported);
            Assert.Contains("DTSTART:" + start.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture) + "\r\n", exported);
            AssertInstants(expected, Assert.Single(IcsService.ParseText(exported).Events), start.AddDays(-1), start.AddYears(4));
        }

        [Fact]
        public void ImportedUtcSeriesKeepsUtcAnchorWithoutTimeZoneDefinition()
        {
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART:20261031T160000Z\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=3\r\n")).Events);
            string exported = IcsService.BuildIcs(new[] { master });
            Assert.DoesNotContain("BEGIN:VTIMEZONE", exported);
            Assert.Contains("DTSTART:20261031T160000Z", exported);
            AssertInstants("20261031T160000Z,20261101T160000Z,20261102T160000Z", Assert.Single(IcsService.ParseText(exported).Events), new DateTime(2026, 10, 30), new DateTime(2026, 11, 4));
        }

        [Theory]
        [InlineData("Europe/London", "20260327T090000", "20260327T090000Z,20260328T090000Z,20260329T080000Z,20260330T080000Z")]
        [InlineData("America/New_York", "20260306T090000", "20260306T140000Z,20260307T140000Z,20260308T130000Z,20260309T130000Z")]
        [InlineData("America/New_York", "20261030T090000", "20261030T130000Z,20261031T130000Z,20261101T140000Z,20261102T140000Z")]
        [InlineData("Australia/Lord_Howe", "20260403T090000", "20260402T220000Z,20260403T220000Z,20260404T223000Z,20260405T223000Z")]
        [InlineData("Europe/Moscow", "20141024T090000", "20141024T050000Z,20141025T050000Z,20141026T060000Z,20141027T060000Z")]
        [InlineData("Europe/Istanbul", "20160325T090000", "20160325T070000Z,20160326T070000Z,20160327T060000Z,20160328T060000Z")]
        public void SourceDefinitionsPreserveSpringFallHalfHourAndBaseOffsetChanges(string zone, string stamp, string expected)
        {
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART;TZID=" + zone + ":" + stamp + "\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=4\r\n")).Events);
            string exported = IcsService.BuildIcs(new[] { master });
            var restored = Assert.Single(IcsService.ParseText(exported).Events);
            AssertInstants(expected, restored, master.Start.AddDays(-2), master.Start.AddDays(6));
            Assert.All(exported.Split(new[] { "\r\n" }, StringSplitOptions.None), line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75));
        }

        [Fact]
        public void SourceZoneDefinitionIsWrittenOnceForSeveralSeries()
        {
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART;TZID=Europe/London:20261024T090000\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=2\r\n")).Events);
            var second = master.Clone();
            second.Id = Guid.NewGuid();
            string exported = IcsService.BuildIcs(new[] { master, second });
            Assert.Equal(1, exported.Split(new[] { "BEGIN:VTIMEZONE" }, StringSplitOptions.None).Length - 1);
            Assert.Equal(2, IcsService.ParseText(exported).Events.Count);
        }

        [Fact]
        public void SourceEndInSecondRepeatedHourPreservesPhysicalDuration()
        {
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART;TZID=America/New_York:20261101T003000\r\nDTEND:20261101T063000Z\r\nRRULE:FREQ=DAILY;COUNT=2\r\n")).Events);
            Assert.Equal(TimeSpan.FromHours(2), master.End.ToUniversalTime() - master.Start.ToUniversalTime());
            var restored = Assert.Single(IcsService.ParseText(IcsService.BuildIcs(new[] { master })).Events);
            Assert.Equal(TimeSpan.FromHours(2), restored.End.ToUniversalTime() - restored.Start.ToUniversalTime());
        }

        [Fact]
        public void SourceDurationOfTwentyFourHoursStaysElapsedAcrossFallBack()
        {
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART;TZID=America/New_York:20261101T003000\r\nDURATION:PT24H\r\nRRULE:FREQ=DAILY;COUNT=2\r\n")).Events);
            string exported = IcsService.BuildIcs(new[] { master });
            Assert.Contains("DURATION:PT", exported);
            Assert.DoesNotContain("DURATION:P1D", exported);
            var restored = Assert.Single(IcsService.ParseText(exported).Events);
            Assert.Equal(new DateTime(2026, 11, 2, 4, 30, 0, DateTimeKind.Utc), restored.End.ToUniversalTime());
            Assert.Equal(TimeSpan.FromHours(24), restored.End.ToUniversalTime() - restored.Start.ToUniversalTime());
        }

        [Fact]
        public void FractionalSourceDurationIsExplicitlyUnsupported()
        {
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART;TZID=UTC:20261031T160000\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=2\r\n")).Events);
            master.End = master.End.AddTicks(1);
            Assert.Contains("fractional seconds", Assert.Throws<NotSupportedException>(() => IcsService.BuildIcs(new[] { master })).Message);
        }

        [Fact]
        public void LongSourceDurationUsesExactHoursWithoutOverflowingSecondsParser()
        {
            var master = Assert.Single(IcsService.ParseText(Wrap("DTSTART;TZID=UTC:19000101T090000\r\nDTEND:20000101T090000Z\r\nRRULE:FREQ=YEARLY;COUNT=2\r\n")).Events);
            var restored = Assert.Single(IcsService.ParseText(IcsService.BuildIcs(new[] { master })).Events);
            Assert.Equal(master.End.ToUniversalTime(), restored.End.ToUniversalTime());
        }

        private static string Wrap(string properties) => "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:export-regression-series\r\n" + properties + "END:VEVENT\r\nEND:VCALENDAR\r\n";

        private static void AssertInstants(string expected, CalendarEvent master, DateTime from, DateTime to)
        {
            var instants = expected.Split(',').Select(value => DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)).ToArray();
            Assert.Equal(instants, Recurrence.Expand(master, from, to).Select(e => e.Start.ToUniversalTime()).ToArray());
        }
    }
}
