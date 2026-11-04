using System;
using System.Globalization;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class IcsExportCultureRegressionTests
    {
        [Fact]
        public void TimedEventExportsGregorianDatesUnderThaiCulture()
        {
            string ics = ExportUnder("th-TH", new CalendarEvent
            {
                Title = "Standup",
                Start = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc)
            });
            Assert.DoesNotContain("2569", ics);
            Assert.Contains("\r\nDTSTART:20260301T100000Z\r\n", ics);
            Assert.Contains("\r\nDTEND:20260301T110000Z\r\n", ics);
        }

        [Fact]
        public void RecurringAllDayEventExportsGregorianDatesUnderThaiCulture()
        {
            string ics = ExportUnder("th-TH", new CalendarEvent
            {
                Title = "Weekly",
                AllDay = true,
                Start = new DateTime(2026, 3, 1),
                End = new DateTime(2026, 3, 2),
                Repeat = RepeatFreq.Weekly,
                RepeatDays = { DayOfWeek.Sunday },
                RepeatUntil = new DateTime(2026, 6, 28),
                SkipDates = { new DateTime(2026, 3, 8) }
            });
            Assert.DoesNotContain("2569", ics);
            Assert.Contains("\r\nDTSTART;VALUE=DATE:20260301\r\n", ics);
            Assert.Contains("UNTIL=20260628", ics);
            Assert.Contains("\r\nEXDATE;VALUE=DATE:20260308\r\n", ics);
        }

        [Fact]
        public void RecurringTimedEventExportsGregorianDatesUnderThaiCulture()
        {
            string ics = ExportUnder("th-TH", new CalendarEvent
            {
                Title = "Weekly timed",
                Start = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc),
                Repeat = RepeatFreq.Weekly,
                RepeatDays = { DayOfWeek.Sunday },
                RepeatUntil = new DateTime(2026, 6, 28),
                ExtraDates = { new DateTime(2026, 3, 4) }
            });
            Assert.DoesNotContain("2569", ics);
            Assert.Contains("UNTIL=2026", ics);
            Assert.Contains("RDATE", ics);
        }

        // KD-J (IE-007): the suggested file name is Gregorian too.
        [Fact]
        public void SuggestedExportNameIsGregorianUnderThaiCulture()
        {
            var oldCurrent = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
                Assert.Equal("killendar-2026-11-03.ics", Killendar.Features.IcsTransfer.SuggestedExportName(new DateTime(2026, 11, 3)));
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCurrent;
            }
        }

        private static string ExportUnder(string culture, params CalendarEvent[] events)
        {
            // Only this thread's culture: the process-wide default would leak into tests running
            // in parallel in other collections.
            var oldCurrent = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                return IcsService.BuildIcs(events);
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCurrent;
            }
        }
    }
}
