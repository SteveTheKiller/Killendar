using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // How long the views' data takes with a big calendar: a month's and a week's appointments,
    // with repeats that move off days off, extra dates, holidays and another calendar shown.
    // Runs only when KILLENDAR_TIMINGS names a file for the results; otherwise it does nothing.
    [Collection("Calendar settings")]
    public sealed class LargeCalendarTimingTests
    {
        [Fact]
        public void TimeTheViews()
        {
            string? output = Environment.GetEnvironmentVariable("KILLENDAR_TIMINGS");
            if (string.IsNullOrWhiteSpace(output)) return;
            var report = new StringBuilder();
            foreach (int size in new[] { 1_000, 10_000, 100_000 })
                Measure(size, report);
            File.WriteAllText(output, report.ToString());
        }

        private static void Measure(int size, StringBuilder report)
        {
            using var fixture = new AppointmentFixture();
            var random = new Random(size);
            var rows = new List<CalendarEvent>(size);
            var origin = new DateTime(2024, 1, 1);
            for (int i = 0; i < size; i++)
            {
                var start = origin.AddDays(random.Next(0, 365 * 5)).AddHours(7 + random.Next(0, 11));
                var ev = new CalendarEvent { Title = "Item " + i, Start = start, End = start.AddMinutes(30 + 30 * random.Next(0, 4)) };
                switch (i % 20)
                {
                    case 0: ev.Repeat = RepeatFreq.Weekly; ev.RepeatDays = [start.DayOfWeek]; break;
                    case 1: ev.Repeat = RepeatFreq.Monthly; ev.RepeatMonthWeek = Math.Min(4, Recurrence.WeekOfMonth(start)); ev.RepeatShift = 1; break;
                    case 2: ev.Repeat = RepeatFreq.Daily; ev.RepeatCount = 30; ev.RepeatShift = -1; break;
                    case 3: ev.Repeat = RepeatFreq.Weekly; ev.ExtraDates = [start.AddDays(3).Date, start.AddDays(40).Date]; break;
                }
                rows.Add(ev);
            }
            var clock = Stopwatch.StartNew();
            fixture.Store.ImportEvents(rows);
            long import = clock.ElapsedMilliseconds;

            // Holidays from the open calendar itself: 12 a year.
            for (int y = 2024; y <= 2029; y++)
                for (int m = 1; m <= 12; m++)
                    fixture.Store.Add(new CalendarEvent { Title = "Holiday", Start = new DateTime(y, m, 15), End = new DateTime(y, m, 16), AllDay = true });
            Holidays.Choose(Path.GetFileName(fixture.Store.FilePath), fixture.Store, Path.GetFileName(fixture.Store.FilePath));
            try
            {
                var month = new DateTime(2026, 11, 1);
                long Time(Func<int> work, int repeats = 5)
                {
                    work();   // first run warms the holiday year cache
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < repeats; i++) work();
                    return sw.ElapsedMilliseconds / repeats;
                }
                long monthMs = Time(() => fixture.Store.GetInRange(month.AddDays(-7), month.AddDays(42)).Count);
                long weekMs = Time(() => fixture.Store.GetInRange(month, month.AddDays(7)).Count);
                // As Month and Week now read it: one range, sorted into days.
                long dayCells = Time(() =>
                {
                    var grid = CalendarLayout.ByDay(fixture.Store.GetInRange(month.AddDays(-7), month.AddDays(35)), month.AddDays(-7), 42);
                    return grid.Values.Sum(day => day.Count);
                }, 2);
                long refreshHolidays = Time(() => { Holidays.Refresh(); return Holidays.IsHoliday(month) ? 1 : 0; });
                report.AppendLine($"{size,7} appointments: import {import} ms, month range {monthMs} ms, week range {weekMs} ms, " +
                                  $"month sorted into 42 days {dayCells} ms, holiday reread {refreshHolidays} ms");
            }
            finally { Holidays.Choose("", fixture.Store, ""); }
        }
    }
}
