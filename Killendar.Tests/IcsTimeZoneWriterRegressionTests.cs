using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-H (IE-001): Windows encodes some transitions at 23:59:59.999 and puts a few rule
    // boundaries within hours of each other. The writer must still describe those zones.
    [Collection("Calendar settings")]
    public sealed class IcsTimeZoneWriterRegressionTests
    {
        /// <summary>Every transition instant (DTSTART and RDATE values) of the parts that go from
        /// <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static List<string> Transitions(string definition, string from, string to)
        {
            var found = new List<string>();
            List<string>? part = null;
            foreach (var line in definition.Replace("\r\n", "\n").Split('\n'))
            {
                if (line == "BEGIN:STANDARD" || line == "BEGIN:DAYLIGHT") { part = []; continue; }
                if (part == null) continue;
                if (line == "END:STANDARD" || line == "END:DAYLIGHT")
                {
                    if (part.Contains("TZOFFSETFROM:" + from) && part.Contains("TZOFFSETTO:" + to))
                        foreach (var item in part)
                        {
                            if (item.StartsWith("DTSTART:", StringComparison.Ordinal)) found.Add(item.Substring(8));
                            else if (item.StartsWith("RDATE:", StringComparison.Ordinal)) found.AddRange(item.Substring(6).Split(','));
                        }
                    part = null;
                    continue;
                }
                part.Add(line);
            }
            return found;
        }

        private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

        [Fact]
        public void BrazilEndOfDaySummerTimeEndsAtLocalMidnight()
        {
            // tzdata: Brazil's 2017-2018 summer time ended at 00:00 local on 18 February 2018.
            string definition = IcsTimeZoneWriter.Build("America/Sao_Paulo", Zone("E. South America Standard Time"));

            Assert.Contains("BEGIN:VTIMEZONE", definition);
            Assert.Contains("20180218T000000", Transitions(definition, "-0200", "-0300"));
            Assert.DoesNotContain("20180217T235959", definition);
        }

        [Fact]
        public void PakistanEndOfDaySummerTimeIsWritten()
        {
            // tzdata (Rule Pakistan 2008): summer time ended 1 November 2008 at 00:00 local, +06 to +05.
            string definition = IcsTimeZoneWriter.Build("Asia/Karachi", Zone("Pakistan Standard Time"));

            Assert.Contains("20081101T000000", Transitions(definition, "+0600", "+0500"));
            Assert.DoesNotContain("20081031T235959", definition);
        }

        [Fact]
        public void BangladeshTransitionsAtTheRuleBoundaryAreWritten()
        {
            // tzdata (Rule Dhaka 2009): summer time ended Dec 31 24:00, that is 1 January 2010
            // at 00:00 on the +07 clock, back to +06.
            string definition = IcsTimeZoneWriter.Build("Asia/Dhaka", Zone("Bangladesh Standard Time"));

            Assert.Contains("20100101T000000", Transitions(definition, "+0700", "+0600"));
        }

        [Fact]
        public void SaoTomeOffsetChangeAnHourAfterARuleStartIsWritten()
        {
            // tzdata (Africa/Sao_Tome): GMT until 2018 Jan 1 01:00, then WAT (+01).
            string definition = IcsTimeZoneWriter.Build("Africa/Sao_Tome", Zone("Sao Tome Standard Time"));

            Assert.Contains("20180101T010000", Transitions(definition, "+0000", "+0100"));
        }

        [Fact]
        public void EverySystemZoneBuildsAndReadsBackWithTheSameOffsets()
        {
            var failures = new List<string>();
            foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
            {
                string definition;
                try { definition = IcsTimeZoneWriter.Build("Probe", zone, 1970, 2040); }
                catch (NotSupportedException ex) { failures.Add(zone.Id + ": " + ex.Message); continue; }

                var lines = definition.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                var read = IcsCustomZone.Read(lines);
                if (read == null) { failures.Add(zone.Id + ": unreadable"); continue; }

                var probes = new List<DateTime>();
                for (var month = new DateTime(1971, 1, 15, 12, 0, 0, DateTimeKind.Utc); month.Year < 2040; month = month.AddMonths(1))
                    probes.Add(month);
                foreach (var line in lines.Where(l => l.StartsWith("RDATE:", StringComparison.Ordinal)))
                    foreach (var value in line.Substring(6).Split(','))
                    {
                        var wall = DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
                        probes.Add(DateTime.SpecifyKind(wall.AddDays(-1), DateTimeKind.Utc));
                        probes.Add(DateTime.SpecifyKind(wall.AddDays(1), DateTimeKind.Utc));
                    }
                foreach (var utc in probes)
                {
                    if (zone.GetUtcOffset(utc) == read.OffsetAt(utc)) continue;
                    failures.Add(zone.Id + ": offset differs at " + utc.ToString("u", CultureInfo.InvariantCulture));
                    break;
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        private static CalendarEvent WeeklyOn(string zoneId)
        {
            var parsed = IcsService.ParseText(
                "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:weekly@example.com\r\nSUMMARY:Weekly\r\n" +
                "DTSTART;TZID=" + zoneId + ":20260310T090000\r\nDTEND;TZID=" + zoneId + ":20260310T100000\r\n" +
                "RRULE:FREQ=WEEKLY;COUNT=5\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
            return Assert.Single(parsed.Events);
        }

        [Fact]
        public void ExportOfASaoPauloSeriesCarriesItsZone()
        {
            var series = WeeklyOn("America/Sao_Paulo");

            string ics = IcsService.BuildIcs(new[] { series });

            Assert.Contains("BEGIN:VTIMEZONE", ics);
            Assert.Contains("DTSTART;TZID=\"America/Sao_Paulo\":20260310T090000", ics);
        }

        [Fact]
        public void ASeriesOnAZoneThatCannotBeWrittenFallsBackToUtcAndIsCounted()
        {
            // A zone this computer cannot name: written on UTC's clock, never a TZID with no VTIMEZONE.
            var series = WeeklyOn("America/Sao_Paulo");
            series.RepeatZoneId = "Nowhere/Unknown";
            var other = WeeklyOn("America/Sao_Paulo");
            other.Id = Guid.NewGuid();
            other.ExternalUid = "fine@example.com";

            string ics = IcsService.BuildIcs(new[] { series, other }, out int fallbacks);

            Assert.Equal(1, fallbacks);
            Assert.DoesNotContain("Nowhere/Unknown", ics);
            // 09:00 at -03:00 in Sao Paulo, which has had no summer time since 2019, is 12:00 UTC.
            Assert.Contains("DTSTART:20260310T120000Z", ics);
            Assert.Contains("DTSTART;TZID=\"America/Sao_Paulo\":20260310T090000", ics);
        }
    }
}
