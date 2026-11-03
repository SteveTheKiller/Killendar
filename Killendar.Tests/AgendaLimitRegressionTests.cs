using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Killendar.Cli;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Runs the agenda in-process only; AgendaCliTests also starts the exe and stays out of local runs.
    public sealed class AgendaLimitRegressionTests
    {
        [Fact]
        public void LimitKeepsTheEarliestDaysWhenAllDayItemsComeLater()
        {
            string path = Path.Combine(TestScratch.Root, "agenda-limit-" + Guid.NewGuid().ToString("N"), "test.kcal");
            try
            {
                var writer = new EventStore();
                writer.Open(path);
                foreach (int hour in new[] { 9, 11, 14 })
                    writer.Add(new CalendarEvent { Title = "Meeting " + hour, Start = new DateTime(2026, 10, 1, hour, 0, 0), End = new DateTime(2026, 10, 1, hour, 30, 0) });
                foreach (int day in new[] { 20, 21, 22 })
                    writer.Add(new CalendarEvent { Title = "Holiday " + day, AllDay = true, Start = new DateTime(2026, 10, day), End = new DateTime(2026, 10, day) });
                writer.Close();

                var titles = Program.Agenda(path, new DateTime(2026, 10, 1), 31, 3)
                    .Select(item => JsonDocument.Parse(JsonSerializer.Serialize(item)).RootElement.GetProperty("title").GetString())
                    .ToArray();

                Assert.Equal(new[] { "Meeting 9", "Meeting 11", "Meeting 14" }, titles);
            }
            finally
            {
                try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void AllDayItemsLeadTheirOwnDay()
        {
            string path = Path.Combine(TestScratch.Root, "agenda-order-" + Guid.NewGuid().ToString("N"), "test.kcal");
            try
            {
                var writer = new EventStore();
                writer.Open(path);
                writer.Add(new CalendarEvent { Title = "Day two timed", Start = new DateTime(2026, 10, 2, 8, 0, 0), End = new DateTime(2026, 10, 2, 9, 0, 0) });
                writer.Add(new CalendarEvent { Title = "Day two all day", AllDay = true, Start = new DateTime(2026, 10, 2), End = new DateTime(2026, 10, 2) });
                writer.Add(new CalendarEvent { Title = "Day one timed", Start = new DateTime(2026, 10, 1, 8, 0, 0), End = new DateTime(2026, 10, 1, 9, 0, 0) });
                writer.Close();

                var titles = Program.Agenda(path, new DateTime(2026, 10, 1), 3, 10)
                    .Select(item => JsonDocument.Parse(JsonSerializer.Serialize(item)).RootElement.GetProperty("title").GetString())
                    .ToArray();

                Assert.Equal(new[] { "Day one timed", "Day two all day", "Day two timed" }, titles);
            }
            finally
            {
                try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { /* best effort */ }
            }
        }
    }
}
