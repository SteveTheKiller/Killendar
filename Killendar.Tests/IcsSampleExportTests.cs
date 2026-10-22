using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Calendar files for checking outside Killendar: each sample's .ics, and beside it the dates
    // Killendar itself shows for it, so an independent reader can compare. Runs only when
    // KILLENDAR_ICS_SAMPLES names a folder; otherwise it does nothing.
    [Collection("Calendar settings")]
    public sealed class IcsSampleExportTests
    {
        private static readonly DateTime From = new(2026, 10, 1), To = new(2027, 12, 31);

        [Fact]
        public void WriteSamples()
        {
            string? folder = Environment.GetEnvironmentVariable("KILLENDAR_ICS_SAMPLES");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            WorkSchedule.Load(null, null);   // Monday to Friday

            var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            var samples = new List<(string Name, List<CalendarEvent> Rows)>();

            samples.Add(("monthly-second-tuesday", [new CalendarEvent
            {
                Title = "Board", Start = new DateTime(2026, 10, 13, 9, 0, 0), End = new DateTime(2026, 10, 13, 10, 0, 0),
                Repeat = RepeatFreq.Monthly, RepeatMonthWeek = 2, RepeatCount = 12,
            }]));
            samples.Add(("monthly-last-friday", [new CalendarEvent
            {
                Title = "Payday drinks", Start = new DateTime(2026, 10, 30, 17, 0, 0), End = new DateTime(2026, 10, 30, 19, 0, 0),
                Repeat = RepeatFreq.Monthly, RepeatMonthWeek = -1, RepeatUntil = new DateTime(2027, 6, 30),
            }]));
            samples.Add(("weekly-extra-and-skipped", [new CalendarEvent
            {
                Title = "Review", Start = new DateTime(2026, 10, 19, 9, 0, 0), End = new DateTime(2026, 10, 19, 10, 0, 0),
                Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Monday, DayOfWeek.Thursday], RepeatCount = 10,
                ExtraDates = [new DateTime(2026, 10, 24), new DateTime(2026, 12, 31)],
                SkipOccurrences = [new DateTime(2026, 10, 29, 9, 0, 0)],
            }]));

            var zoned = new CalendarEvent { Title = "Call with Tokyo", Start = TimeZones.FromZone(new DateTime(2026, 10, 20, 9, 0, 0), tokyo),
                Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Tuesday], RepeatCount = 30 };
            zoned.End = zoned.Start.AddHours(1);
            TimeZones.Apply(zoned, tokyo.Id);
            samples.Add(("weekly-tokyo-across-clock-change", [zoned]));

            var longCall = new CalendarEvent { Title = "Long call", Start = TimeZones.FromZone(new DateTime(2026, 10, 21, 18, 0, 0), tokyo),
                Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Wednesday], RepeatCount = 3 };
            longCall.End = longCall.Start.AddMinutes(90).AddSeconds(30);
            TimeZones.Apply(longCall, tokyo.Id);
            samples.Add(("ninety-minutes-and-thirty-seconds", [longCall]));
            samples.Add(("moves-off-weekends", [new CalendarEvent
            {
                Title = "Rent", Start = new DateTime(2026, 11, 1, 9, 0, 0), End = new DateTime(2026, 11, 1, 9, 30, 0),
                Repeat = RepeatFreq.Monthly, RepeatCount = 14, RepeatShift = 1,
            }]));
            samples.Add(("yearly-all-day-with-extra", [new CalendarEvent
            {
                Title = "Anniversary", Start = new DateTime(2026, 11, 3), End = new DateTime(2026, 11, 4), AllDay = true,
                Repeat = RepeatFreq.Yearly, ExtraDates = [new DateTime(2027, 5, 3)], RepeatCount = 3,
            }]));

            var meeting = new CalendarEvent
            {
                Title = "Planning, Q4; budget \\ scope", Description = "Agenda:\nOne, two; three",
                Location = "Room 4, \"Big\" one", Start = new DateTime(2026, 10, 21, 14, 0, 0), End = new DateTime(2026, 10, 21, 15, 0, 0),
                Repeat = RepeatFreq.Daily, RepeatCount = 5, ReminderMinutes = 15, Attendees = ["pat@example.com", "sam@example.com"],
                Categories = "Work",
                KeptProperties = ["ORGANIZER;CN=\"Boss, The\":mailto:boss@example.com",
                    "ATTENDEE;CN=Pat;PARTSTAT=ACCEPTED:mailto:pat@example.com", "SEQUENCE:2"],
            };
            var moved = Recurrence.Expand(meeting, new DateTime(2026, 10, 23), new DateTime(2026, 10, 24)).Single();
            moved.Id = Guid.NewGuid();
            moved.Start = moved.Start.AddHours(2);
            moved.End = moved.End.AddHours(2);
            moved.Title = "Planning (moved)";
            moved.Repeat = RepeatFreq.None;
            samples.Add(("meeting-with-moved-date-and-escaping", [meeting, moved]));

            samples.Add(("long-unicode-folding", [new CalendarEvent
            {
                Title = new string('é', 40) + " 日本語のタイトル " + new string('x', 30),
                Description = string.Concat(Enumerable.Repeat("Zażółć gęślą jaźń. ", 12)),
                Start = new DateTime(2026, 10, 22, 8, 0, 0), End = new DateTime(2026, 10, 22, 9, 0, 0),
            }]));

            var manifest = new StringBuilder("{");
            bool first = true;
            foreach (var (name, rows) in samples)
            {
                File.WriteAllText(Path.Combine(folder, name + ".ics"), IcsService.BuildIcs(rows), new UTF8Encoding(false));
                // What Killendar shows for it: the repeats expanded, with single-date edits in place.
                var shown = new List<CalendarEvent>();
                foreach (var master in rows.Where(r => r.IsSeries))
                {
                    var edits = rows.Where(r => r.SeriesId == master.Id).ToList();
                    foreach (var occurrence in Recurrence.Expand(master, From, To))
                    {
                        if (edits.Any(e => Recurrence.OccurrenceKey(master, e) == Recurrence.OccurrenceKey(master, occurrence))) continue;
                        shown.Add(occurrence);
                    }
                    shown.AddRange(edits);
                }
                shown.AddRange(rows.Where(r => !r.IsSeries && r.SeriesId == null));
                // Each as "local wall time|UTC", so a reader can compare floating times by the wall
                // clock and zoned or UTC ones by the instant.
                var instants = shown.OrderBy(e => e.Start).Select(e => e.AllDay
                    ? e.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : e.Start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "|" +
                      e.Start.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                manifest.Append(first ? "" : ",").Append('"').Append(name).Append("\":[")
                        .Append(string.Join(",", instants.Select(i => "\"" + i + "\""))).Append(']');
                first = false;
            }
            manifest.Append('}');
            File.WriteAllText(Path.Combine(folder, "expected.json"), manifest.ToString());
        }
    }
}
