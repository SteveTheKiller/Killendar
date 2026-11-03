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
    // IE-008 (the CLI agenda honors the saved work week) and UI-002 (the agenda's Edit loads the
    // current row). Neither opens a window.
    [Collection("Calendar settings")]
    public sealed class AgendaStaleStateRegressionTests
    {
        [Fact]
        public void AgendaShiftsARepeatOffTheSavedDaysOff()
        {
            string? savedDays = Settings.Get(WorkSchedule.DaysSetting);
            string path = Path.Combine(TestScratch.Root, "agenda-shift-" + Guid.NewGuid().ToString("N"), "test.kcal");
            try
            {
                Settings.Set(WorkSchedule.DaysSetting, "0,1,2,3,4");   // Sunday to Thursday
                WorkSchedule.Load(null, null);
                var writer = new EventStore();
                writer.Open(path);
                writer.Add(new CalendarEvent
                {
                    Title = "Friday report", Start = new DateTime(2026, 10, 2, 9, 0, 0), End = new DateTime(2026, 10, 2, 10, 0, 0),
                    Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Friday], RepeatCount = 1, RepeatShift = 1,
                });
                writer.Close();

                var starts = Program.Agenda(path, new DateTime(2026, 10, 1), 7, 10)
                    .Select(item => JsonDocument.Parse(JsonSerializer.Serialize(item)).RootElement.GetProperty("start").GetString()!.Substring(0, 10))
                    .ToArray();

                Assert.Equal(new[] { "2026-10-04" }, starts);
            }
            finally
            {
                Settings.Set(WorkSchedule.DaysSetting, savedDays ?? "");
                WorkSchedule.Load(null, null);
                try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void EditReloadsTheCurrentRowInsteadOfTheCapturedOne()
        {
            var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var captured = new CalendarEvent { Id = id, Title = "Old", Start = new DateTime(2026, 10, 1, 9, 0, 0), End = new DateTime(2026, 10, 1, 10, 0, 0) };
            var moved = new CalendarEvent { Id = id, Title = "Old", Start = new DateTime(2026, 10, 3, 9, 0, 0), End = new DateTime(2026, 10, 3, 10, 0, 0) };

            Assert.Same(moved, Shell.MainWindow.CurrentRow((i, o) => i == id ? moved : null, captured));
            Assert.Same(captured, Shell.MainWindow.CurrentRow((i, o) => null, captured));
        }
    }
}
