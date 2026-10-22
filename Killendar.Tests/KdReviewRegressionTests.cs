using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-D review fixes.
    [Collection("Calendar settings")]
    public sealed class KdReviewRegressionTests : IDisposable
    {
        public KdReviewRegressionTests() => WorkSchedule.Load(null, null);   // Monday to Friday
        public void Dispose() => WorkSchedule.Load(null, null);

        private static readonly DateTime Saturday = new(2026, 10, 24, 9, 0, 0);

        private static CalendarEvent Saturdays() => new()
        {
            Title = "Market", Start = Saturday, End = Saturday.AddHours(1), Repeat = RepeatFreq.Weekly,
            RepeatDays = [DayOfWeek.Saturday], RepeatShift = 1,
        };

        // BF-001
        [Fact]
        public void EditingTheSeriesFromAMovedDateDoesNotMoveTheSeries()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Saturdays());
            var shown = fixture.Store.GetInRange(Saturday.Date, Saturday.Date.AddDays(7)).Single().Clone();
            Assert.Equal(DayOfWeek.Monday, shown.Start.DayOfWeek);
            shown.Title = "Market, renamed";

            fixture.Store.UpdateSeries(shown);

            var master = Assert.Single(fixture.Store.Events);
            Assert.Equal(Saturday, master.Start);
            Assert.Equal("Market, renamed", master.Title);
        }

        // BF-009
        [Fact]
        public void ADailyRepeatDoesNotStackItsWeekendOnMonday()
        {
            var daily = new CalendarEvent
            {
                Title = "Log", Start = new DateTime(2026, 10, 19, 9, 0, 0), End = new DateTime(2026, 10, 19, 9, 30, 0),
                Repeat = RepeatFreq.Daily, RepeatShift = 1,
            };
            var days = Recurrence.Expand(daily, new DateTime(2026, 10, 19), new DateTime(2026, 10, 27)).Select(o => o.Start.Date).ToList();
            Assert.Equal(days.Distinct().Count(), days.Count);
            Assert.DoesNotContain(days, d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        }

        // BF-002 and BF-004
        [Fact]
        public void ExtraDatesDoNotCountTowardTheSplitAndCannotBeSplitAt()
        {
            using var fixture = new AppointmentFixture();
            var start = new DateTime(2026, 10, 19, 9, 0, 0);
            fixture.Store.Add(new CalendarEvent
            {
                Title = "Review", Start = start, End = start.AddHours(1), Repeat = RepeatFreq.Weekly,
                RepeatDays = [DayOfWeek.Monday], RepeatCount = 4, ExtraDates = [new DateTime(2026, 10, 21)],
            });
            var occurrences = fixture.Store.GetInRange(start.Date, start.Date.AddDays(40)).OrderBy(o => o.Start).ToList();
            Assert.False(fixture.Store.CanSplit(occurrences.Single(o => o.Start.Day == 21)));

            var third = occurrences.Single(o => o.Start == start.AddDays(14)).Clone();
            third.Title = "Review, later";
            fixture.Store.UpdateFollowing(third);

            var all = fixture.Store.GetInRange(start.Date, start.Date.AddDays(40)).OrderBy(o => o.Start).Select(o => (o.Start.Day, o.Title)).ToList();
            Assert.Equal([(19, "Review"), (21, "Review"), (26, "Review"), (2, "Review, later"), (9, "Review, later")], all);
        }

        private static string Invite(int sequence, string start, string title, string uid = "planning@example.com", string? extra = null, string? method = null)
        {
            var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0" };
            if (method != null) lines.Add("METHOD:" + method);
            lines.AddRange(new[] { "BEGIN:VEVENT", "UID:" + uid, "SEQUENCE:" + sequence, "DTSTART:" + start,
                "DTEND:" + start.Substring(0, 9) + "100000", "SUMMARY:" + title, "ORGANIZER:mailto:boss@example.com" });
            if (extra != null) lines.Add(extra);
            lines.AddRange(new[] { "END:VEVENT", "END:VCALENDAR", "" });
            return string.Join("\r\n", lines);
        }

        private static ImportPlan Import(EventStore store, string ics)
        {
            var parsed = IcsService.ParseText(ics);
            var plan = store.PlanImport(parsed.Events, parsed.CanceledEvents, parsed.CanceledOccurrences);
            store.WriteImport(plan, CancellationToken.None);
            store.PublishImport(plan);
            return plan;
        }

        // BF-007
        [Fact]
        public void AnOlderCancellationIsIgnored()
        {
            using var fixture = new AppointmentFixture();
            Import(fixture.Store, Invite(3, "20261022T090000", "Planning"));

            var plan = Import(fixture.Store, Invite(2, "20261022T090000", "Planning", extra: "STATUS:CANCELLED", method: "CANCEL"));

            Assert.Equal(0, plan.Cancellations);
            Assert.Single(fixture.Store.Events);
        }

        // BF-005
        [Fact]
        public void EachCanceledMeetingIsItsOwnRecentlyDeletedEntry()
        {
            using var fixture = new AppointmentFixture();
            Import(fixture.Store, Invite(0, "20261022T090000", "Planning", "a@example.com"));
            Import(fixture.Store, Invite(0, "20261023T090000", "Budget", "b@example.com"));
            string both = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "METHOD:CANCEL",
                "BEGIN:VEVENT", "UID:a@example.com", "SEQUENCE:1", "STATUS:CANCELLED", "DTSTART:20261022T090000", "SUMMARY:Planning", "END:VEVENT",
                "BEGIN:VEVENT", "UID:b@example.com", "SEQUENCE:1", "STATUS:CANCELLED", "DTSTART:20261023T090000", "SUMMARY:Budget", "END:VEVENT",
                "END:VCALENDAR", "");

            Import(fixture.Store, both);

            var titles = fixture.Store.ListDeleted().Select(d => d.Title).OrderBy(t => t).ToList();
            Assert.Equal(["Budget", "Planning"], titles);
        }

        // BF-006
        [Fact]
        public void AMovedSeriesDropsTheOneDateEditsThatNoLongerFit()
        {
            using var fixture = new AppointmentFixture();
            Import(fixture.Store, Invite(0, "20261019T090000", "Standup", extra: "RRULE:FREQ=DAILY;COUNT=5"));
            var second = fixture.Store.GetInRange(new DateTime(2026, 10, 20), new DateTime(2026, 10, 21)).Single().Clone();
            second.Title = "Standup, room 2";
            fixture.Store.SaveOccurrence(second);

            Import(fixture.Store, Invite(1, "20261019T100000", "Standup", extra: "RRULE:FREQ=DAILY;COUNT=5"));

            var shown = fixture.Store.GetInRange(new DateTime(2026, 10, 19), new DateTime(2026, 10, 24)).OrderBy(o => o.Start).ToList();
            Assert.Equal(5, shown.Count);
            Assert.All(shown, o => Assert.Equal(10, o.Start.Hour));
        }

        // BF-010
        [Fact]
        public void KillendarsOwnMovedDatesDoNotComeBackAsEdits()
        {
            var series = Saturdays();
            series.Start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1, 9, 0, 0);
            while (series.Start.DayOfWeek != DayOfWeek.Saturday) series.Start = series.Start.AddDays(1);
            series.End = series.Start.AddHours(1);
            string text = IcsService.BuildIcs([series]);
            Assert.Contains("X-KILLENDAR-MOVED:1", text);

            var back = IcsService.ParseText(text).Events;

            var master = Assert.Single(back);
            Assert.Equal(1, master.RepeatShift);
        }

        // BF-013 and BF-016
        [Fact]
        public void AZoneRuleEndingInUtcKeepsItsLastChange()
        {
            string zone = string.Join("\n",
                "TZID;X-NOTE=old:Old Rules", "BEGIN:STANDARD", "DTSTART:19701025T030000",
                "RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10;UNTIL=19961027T010000Z", "TZOFFSETFROM:+0300", "TZOFFSETTO:+0200",
                "END:STANDARD", "BEGIN:DAYLIGHT", "DTSTART:19700329T020000", "RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=3",
                "TZOFFSETFROM:+0200", "TZOFFSETTO:+0300", "END:DAYLIGHT");
            var parsed = IcsCustomZone.Read([.. zone.Split('\n')]);
            Assert.NotNull(parsed);
            Assert.Equal("Old Rules", parsed!.Id);
            // After 27 October 1996 03:00 (+03:00) the zone is on +02:00.
            Assert.Equal(TimeSpan.FromHours(2), parsed.OffsetAt(new DateTime(1996, 11, 1, 12, 0, 0, DateTimeKind.Utc)));
        }

        // Calendar file check: lengths in the RFC 5545 grammar, and files written the old way still read.
        [Theory]
        [InlineData(60, "PT1H")]
        [InlineData(90, "PT1H30M")]
        [InlineData(45, "PT45M")]
        [InlineData(1440, "PT24H")]
        public void LengthsAreWrittenInTheStandardsGrammar(int minutes, string expected)
            => Assert.Equal(expected, IcsService.TimeDuration(TimeSpan.FromMinutes(minutes)));

        [Fact]
        public void SecondsAfterHoursStillRead()
        {
            Assert.Equal("PT1H0M30S", IcsService.TimeDuration(TimeSpan.FromSeconds(3630)));
            string ics = string.Join("\r\n", "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:old@example.com",
                "DTSTART;TZID=Tokyo Standard Time:20261020T090000", "DURATION:PT1H0S", "RRULE:FREQ=WEEKLY;COUNT=2",
                "SUMMARY:Old export", "END:VEVENT", "END:VCALENDAR", "");
            var ev = Assert.Single(IcsService.ParseText(ics).Events);
            Assert.Equal(TimeSpan.FromHours(1), ev.End.ToUniversalTime() - ev.Start.ToUniversalTime());
        }
    }
}
