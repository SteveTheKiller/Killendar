using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S10/S11: reminders fire while Killendar runs, missed ones show at the next launch, and
    // dismissing or snoozing is kept in the calendar file so it survives a restart.
    [Collection("Calendar settings")]
    public sealed class ReminderRegressionTests
    {
        private static readonly DateTime Now = new(2026, 10, 21, 9, 0, 0);

        private static CalendarEvent Meeting(string title, DateTime start, int? reminder, bool allDay = false) => new()
        {
            Title = title,
            Start = start,
            End = allDay ? start.AddDays(1) : start.AddHours(1),
            AllDay = allDay,
            ReminderMinutes = reminder,
        };

        [Fact]
        public void TimedReminderFiresItsMinutesBeforeTheStart()
            => Assert.Equal(new DateTime(2026, 10, 21, 9, 45, 0),
                Reminders.FireTime(Meeting("Standup", new DateTime(2026, 10, 21, 10, 0, 0), 15)));

        [Fact]
        public void AllDayReminderFiresAtNineTheDayBefore()
            => Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0),
                Reminders.FireTime(Meeting("Holiday", new DateTime(2026, 10, 22), 0, allDay: true)));

        [Fact]
        public void NoReminderNeverFires()
            => Assert.Null(Reminders.FireTime(Meeting("Quiet", Now, null)));

        [Fact]
        public void DueListsOnlyRemindersWhoseTimeHasCome()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Meeting("Soon", Now.AddMinutes(10), 15));
            fixture.Store.Add(Meeting("Later", Now.AddHours(2), 15));
            fixture.Store.Add(Meeting("None", Now.AddMinutes(5), null));

            Assert.Equal("Soon", Assert.Single(fixture.Store.DueReminders(Now)).Title);
        }

        [Fact]
        public void RepeatingAppointmentRemindsForEachOccurrence()
        {
            using var fixture = new AppointmentFixture();
            var daily = Meeting("Daily", new DateTime(2026, 10, 1, 9, 10, 0), 15);
            daily.Repeat = RepeatFreq.Daily;
            fixture.Store.Add(daily);

            var due = Assert.Single(fixture.Store.DueReminders(Now));
            Assert.Equal(new DateTime(2026, 10, 21, 9, 10, 0), due.Start);
        }

        [Fact]
        public void MissedReminderShowsAtLaunchUntilTheAppointmentIsLongOver()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Meeting("Missed this morning", Now.AddHours(-2), 15));
            fixture.Store.Add(Meeting("Missed last week", Now.AddDays(-7), 15));

            Assert.Equal("Missed this morning", Assert.Single(fixture.Store.DueReminders(Now)).Title);
        }

        [Fact]
        public void DismissedAndSnoozedRemindersSurviveReopening()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Meeting("Dismiss me", Now.AddMinutes(10), 15));
            fixture.Store.Add(Meeting("Snooze me", Now.AddMinutes(10), 15));
            var due = fixture.Store.DueReminders(Now);
            fixture.Store.DismissReminder(due.Single(d => d.Title == "Dismiss me"));
            fixture.Store.SnoozeReminder(due.Single(d => d.Title == "Snooze me"), Now.AddMinutes(5));

            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);

            Assert.Empty(fixture.Store.DueReminders(Now.AddMinutes(4)));
            Assert.Equal("Snooze me", Assert.Single(fixture.Store.DueReminders(Now.AddMinutes(5))).Title);
        }

        [Fact]
        public void ReminderIsSavedWithTheAppointment()
        {
            using var fixture = new AppointmentFixture();
            var ev = Meeting("Kept", Now.AddDays(1), 30);
            fixture.Store.Add(ev);
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);

            Assert.Equal(30, Assert.Single(fixture.Store.Events).ReminderMinutes);
        }

        [Theory]
        [InlineData("TRIGGER:-PT15M", 15)]
        [InlineData("TRIGGER;RELATED=START:-PT1H", 60)]
        [InlineData("TRIGGER:-P1D", 1440)]
        [InlineData("TRIGGER:-P1DT2H30M", 1590)]
        [InlineData("TRIGGER:PT0S", 0)]
        [InlineData("TRIGGER:PT15M", null)]                         // after the start
        [InlineData("TRIGGER;RELATED=END:-PT5M", null)]
        [InlineData("TRIGGER;VALUE=DATE-TIME:20261021T080000Z", null)]
        public void ImportedAlarmBecomesTheReminder(string trigger, int? minutes)
        {
            string ics = string.Join("\r\n",
                "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:alarm@example.com",
                "DTSTART:20261022T090000", "DTEND:20261022T100000", "SUMMARY:With alarm",
                "BEGIN:VALARM", "ACTION:DISPLAY", "DESCRIPTION:Reminder", trigger, "END:VALARM",
                "END:VEVENT", "END:VCALENDAR", "");

            var ev = Assert.Single(IcsService.ParseText(ics).Events);

            Assert.Equal(minutes, ev.ReminderMinutes);
        }

        [Fact]
        public void ExportWritesAnAlarmForAReminderSetHere()
        {
            var ev = Meeting("Set here", Now.AddDays(1), 10);

            string ics = IcsService.BuildIcs([ev]);

            Assert.Contains("BEGIN:VALARM", ics);
            Assert.Contains("TRIGGER:-PT10M", ics);
            var back = Assert.Single(IcsService.ParseText(ics).Events);
            Assert.Equal(10, back.ReminderMinutes);
            Assert.Equal(2, back.KeptProperties.Count(l => l.StartsWith("BEGIN:VALARM") || l.StartsWith("END:VALARM")));
        }

        [Fact]
        public void ChangingAnImportedReminderReplacesItsAlarm()
        {
            var ev = Assert.Single(IcsService.ParseText(string.Join("\r\n",
                "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:alarm@example.com",
                "DTSTART:20261022T090000", "DTEND:20261022T100000", "SUMMARY:With alarm",
                "BEGIN:VALARM", "ACTION:DISPLAY", "TRIGGER:-PT15M", "END:VALARM",
                "END:VEVENT", "END:VCALENDAR", "")).Events);
            ev.ReminderMinutes = 60;

            IcsService.DropStaleAlarms(ev, previousReminder: 15);
            string ics = IcsService.BuildIcs([ev]);

            Assert.DoesNotContain("TRIGGER:-PT15M", ics);
            Assert.Contains("TRIGGER:-PT1H", ics);
        }

        [Fact]
        public void EditorSavesTheChosenReminder()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldTitle = "With reminder";
            fixture.View.FieldReminder = 5;

            fixture.Editor.Save();

            Assert.Equal(5, Assert.Single(fixture.Store.Events).ReminderMinutes);
        }
    }
}
