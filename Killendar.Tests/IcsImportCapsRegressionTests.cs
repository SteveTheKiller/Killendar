using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-E (IE-004, IE-006, IE-011, IE-014, IE-015, IE-016): import parser limits and counts.
    [Collection("Calendar settings")]
    public sealed class IcsImportCapsRegressionTests
    {
        private static string Calendar(params string[] eventLines) =>
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:caps@example.com\r\nSUMMARY:Caps\r\n" +
            string.Join("\r\n", eventLines) + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        private static string DateList(int count)
        {
            var sb = new StringBuilder("RDATE;VALUE=DATE:");
            var day = new DateTime(2027, 1, 1);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(day.AddDays(i).ToString("yyyyMMdd"));
            }
            return sb.ToString();
        }

        [Fact]
        public void ALongRDateListIsCappedReportedAndFast()
        {
            var watch = Stopwatch.StartNew();
            var parsed = IcsService.ParseText(Calendar("DTSTART:20261019T090000", "DTEND:20261019T100000",
                "RRULE:FREQ=WEEKLY;COUNT=2", DateList(20000)));
            watch.Stop();

            var ev = Assert.Single(parsed.Events);
            Assert.Equal(10000, ev.ExtraDates.Count);
            Assert.Equal(1, parsed.LostDetails);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "took " + watch.Elapsed);
        }

        [Fact]
        public void ACanceledTokenStopsALongRDateList()
        {
            using var source = new System.Threading.CancellationTokenSource();
            source.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => IcsService.ParseText(Calendar(
                "DTSTART:20261019T090000", "DTEND:20261019T100000", DateList(20000)), source.Token));
        }

        // KD-I (IE-002): one zone part with a huge RDATE list.
        private static System.Collections.Generic.List<string> ZoneWithRDates(int count)
        {
            var block = new System.Collections.Generic.List<string>
                { "TZID:Made Up Long", "BEGIN:STANDARD", "DTSTART:19700101T000000", "TZOFFSETFROM:+0100", "TZOFFSETTO:+0000" };
            var day = new DateTime(1971, 1, 1, 2, 0, 0);
            for (int i = 0; i < count; i++) block.Add("RDATE:" + day.AddDays(i).ToString("yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            block.Add("END:STANDARD");
            return block;
        }

        [Fact]
        public void AZoneWithAHugeRDateListReadsQuickly()
        {
            string Text(int count) => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VTIMEZONE\r\n" + string.Join("\r\n", ZoneWithRDates(count)) +
                "\r\nEND:VTIMEZONE\r\nBEGIN:VEVENT\r\nUID:long@example.com\r\nSUMMARY:Long\r\n" +
                "DTSTART;TZID=Made Up Long:20260715T090000\r\nDTEND;TZID=Made Up Long:20260715T100000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            IcsService.ParseText(Text(10));   // first use of the zone lookups, so the timing below is the parse alone
            string text = Text(200000);

            var watch = Stopwatch.StartNew();
            var parsed = IcsService.ParseText(text);
            watch.Stop();

            Assert.Single(parsed.Events);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "took " + watch.Elapsed);
        }

        [Fact]
        public void ACanceledTokenStopsReadingACustomZone()
        {
            using var source = new System.Threading.CancellationTokenSource();
            source.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => IcsCustomZone.Read(ZoneWithRDates(5000), source.Token));
        }

        [Fact]
        public void CustomZonesBeyondTheCapAreRefusedLikeUnreadableOnes()
        {
            var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n");
            for (int i = 1; i <= 300; i++)
                sb.Append("BEGIN:VTIMEZONE\r\nTZID:Made Up " + i + "\r\nBEGIN:STANDARD\r\nDTSTART:19701025T030000\r\n" +
                    "RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10\r\nTZOFFSETFROM:+0215\r\nTZOFFSETTO:+0115\r\nEND:STANDARD\r\n" +
                    "BEGIN:DAYLIGHT\r\nDTSTART:19700329T020000\r\nRRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=3\r\n" +
                    "TZOFFSETFROM:+0115\r\nTZOFFSETTO:+0215\r\nEND:DAYLIGHT\r\nEND:VTIMEZONE\r\n");
            string zones = sb.ToString();
            string Event(int n) => zones + "BEGIN:VEVENT\r\nUID:z@example.com\r\nSUMMARY:Z\r\n" +
                "DTSTART;TZID=Made Up " + n + ":20260715T090000\r\nDTEND;TZID=Made Up " + n + ":20260715T100000\r\n" +
                "END:VEVENT\r\nEND:VCALENDAR\r\n";

            Assert.Single(IcsService.ParseText(Event(256)).Events);
            Assert.ThrowsAny<Exception>(() => IcsService.ParseText(Event(300)));
        }

        [Fact]
        public void WeeklyRuleThatSkipsTheStartWeekdayIsCounted()
        {
            var parsed = IcsService.ParseText(Calendar("DTSTART:20261019T090000", "DTEND:20261019T100000",
                "RRULE:FREQ=WEEKLY;BYDAY=WE,FR;COUNT=4"));   // 2026-10-19 is a Monday

            Assert.Single(parsed.Events);
            Assert.Equal(1, parsed.Repeating);
        }

        [Fact]
        public void WeeklyRuleThatIncludesTheStartWeekdayIsNotCounted()
        {
            var parsed = IcsService.ParseText(Calendar("DTSTART:20261019T090000", "DTEND:20261019T100000",
                "RRULE:FREQ=WEEKLY;BYDAY=MO,FR;COUNT=4"));

            Assert.Equal(0, parsed.Repeating);
        }

        [Fact]
        public void CreatedWinsOverDtStampInEitherOrder()
        {
            var stampLast = Assert.Single(IcsService.ParseText(Calendar("DTSTART:20261019T090000", "DTEND:20261019T100000",
                "CREATED:20260101T080000", "DTSTAMP:20260301T080000")).Events);
            var stampFirst = Assert.Single(IcsService.ParseText(Calendar("DTSTART:20261019T090000", "DTEND:20261019T100000",
                "DTSTAMP:20260301T080000", "CREATED:20260101T080000")).Events);
            var stampOnly = Assert.Single(IcsService.ParseText(Calendar("DTSTART:20261019T090000", "DTEND:20261019T100000",
                "DTSTAMP:20260301T080000")).Events);

            Assert.Equal(new DateTime(2026, 1, 1, 8, 0, 0), stampLast.Created);
            Assert.Equal(new DateTime(2026, 1, 1, 8, 0, 0), stampFirst.Created);
            Assert.Equal(new DateTime(2026, 3, 1, 8, 0, 0), stampOnly.Created);
        }

        [Fact]
        public void AnEventOpenAtEndOfFileIsCountedUnreadable()
        {
            var parsed = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:open@example.com\r\nSUMMARY:Open\r\n" +
                "DTSTART:20261019T090000\r\nDTEND:20261019T100000\r\n");

            Assert.Empty(parsed.Events);
            Assert.Equal(1, parsed.Unreadable);
        }

        [Fact]
        public void ATimedExclusionOnAnAllDaySeriesKeepsTheDateAndIsNotCountedAsLost()
        {
            var parsed = IcsService.ParseText(Calendar("DTSTART;VALUE=DATE:20261019", "DTEND;VALUE=DATE:20261020",
                "RRULE:FREQ=WEEKLY;COUNT=3", "EXDATE:20261026T090000"));

            var ev = Assert.Single(parsed.Events);
            Assert.Equal([new DateTime(2026, 10, 26)], ev.SkipDates);
            Assert.Equal(0, parsed.Repeating);
        }
    }
}
