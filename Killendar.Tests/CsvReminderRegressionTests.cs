using System;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Reminders travel in CSV the way Outlook writes them: Reminder on/off, Reminder Date and
    // Reminder Time, the moment the reminder fires.
    public sealed class CsvReminderRegressionTests
    {
        [Fact]
        public void ExportWritesOutlookReminderColumns()
        {
            var ev = new CalendarEvent
            {
                Title = "Call", Start = new DateTime(2026, 10, 22, 9, 0, 0),
                End = new DateTime(2026, 10, 22, 10, 0, 0), ReminderMinutes = 15,
            };

            string csv = CsvService.BuildCsv([ev]);

            Assert.StartsWith("Subject,Start Date,Start Time,End Date,End Time,All day event,Description,Location,Categories,Reminder on/off,Reminder Date,Reminder Time\r\n", csv);
            Assert.Contains(",TRUE,2026-10-22,08:45:00\r\n", csv);
        }

        [Fact]
        public void NoReminderExportsOff()
        {
            var ev = new CalendarEvent
            {
                Title = "Quiet", Start = new DateTime(2026, 10, 22, 9, 0, 0), End = new DateTime(2026, 10, 22, 10, 0, 0),
            };

            Assert.EndsWith(",FALSE,,\r\n", CsvService.BuildCsv([ev]));
        }

        [Fact]
        public void ImportReadsTheReminderBack()
        {
            var ev = new CalendarEvent
            {
                Title = "Round trip", Start = new DateTime(2026, 10, 22, 9, 0, 0),
                End = new DateTime(2026, 10, 22, 10, 0, 0), ReminderMinutes = 60,
            };

            var back = Assert.Single(CsvService.ParseText(CsvService.BuildCsv([ev])).Events);

            Assert.Equal(60, back.ReminderMinutes);
        }

        [Theory]
        [InlineData("TRUE,2026-10-22,08:50:00", 10)]
        [InlineData("TRUE,,", 15)]            // Outlook's default when only the switch is set
        [InlineData("FALSE,2026-10-22,08:50:00", null)]
        [InlineData("TRUE,2026-10-22,09:30:00", null)]   // after the start
        public void ImportReadsOutlookRows(string reminder, int? minutes)
        {
            string csv = "Subject,Start Date,Start Time,End Date,End Time,All day event,Description,Location,Categories,Reminder on/off,Reminder Date,Reminder Time\r\n" +
                         "Meeting,2026-10-22,09:00:00,2026-10-22,10:00:00,FALSE,,,," + reminder + "\r\n";

            Assert.Equal(minutes, Assert.Single(CsvService.ParseText(csv).Events).ReminderMinutes);
        }
    }
}
