using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-D: a holiday calendar, and repeats that move off days off.
    [Collection("Calendar settings")]
    public sealed class HolidayShiftRegressionTests : IDisposable
    {
        private readonly string _dir = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
        private readonly string? _oldDir = Settings.Get("KillendarDataDir");
        private readonly string? _oldHolidays = Settings.Get(Holidays.Setting);
        private readonly EventStore _open = new();

        public HolidayShiftRegressionTests()
        {
            Directory.CreateDirectory(_dir);
            Settings.Set("KillendarDataDir", _dir);
            WorkSchedule.Load(null, null);   // Monday to Friday
            _open.Open(Path.Combine(_dir, "Home.kcal"));
        }

        public void Dispose()
        {
            Holidays.Choose("", _open, "Home.kcal");
            _open.Close();
            Settings.Set("KillendarDataDir", _oldDir ?? "");
            Settings.Set(Holidays.Setting, _oldHolidays ?? "");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private void HolidayFile(string? password = null)
        {
            var store = new EventStore();
            store.Open(Path.Combine(_dir, "Holidays.kcal"));
            store.Add(new CalendarEvent { Title = "Christmas Day", Start = new DateTime(2026, 12, 25), End = new DateTime(2026, 12, 26), AllDay = true });
            store.Add(new CalendarEvent { Title = "Office party", Start = new DateTime(2026, 12, 18, 17, 0, 0), End = new DateTime(2026, 12, 18, 20, 0, 0) });
            if (password != null) store.SetPassword(password);
            store.Close();
        }

        private static CalendarEvent Fridays(int shift) => new()
        {
            Title = "Report", Start = new DateTime(2026, 12, 18, 9, 0, 0), End = new DateTime(2026, 12, 18, 10, 0, 0),
            Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Friday], RepeatCount = 3, RepeatShift = shift,
        };

        private static System.Collections.Generic.List<DateTime> Days(CalendarEvent master) =>
            Recurrence.Expand(master, new DateTime(2026, 12, 1), new DateTime(2027, 2, 1)).Select(o => o.Start.Date).ToList();

        [Fact]
        public void AllDayAppointmentsInTheChosenCalendarAreHolidays()
        {
            HolidayFile();
            Holidays.Choose("Holidays.kcal", _open, "Home.kcal");

            Assert.Equal(Holidays.State.Ready, Holidays.Status);
            Assert.Equal("Christmas Day", Holidays.NameOf(new DateTime(2026, 12, 25)));
            Assert.False(Holidays.IsHoliday(new DateTime(2026, 12, 18)));   // a timed one is not a holiday
            Assert.True(WorkSchedule.IsDayOff(new DateTime(2026, 12, 25)));
        }

        [Theory]
        [InlineData(1, 28)]    // the next workday: Monday
        [InlineData(-1, 24)]   // the workday before: Thursday
        public void ADateOnAHolidayMoves(int shift, int day)
        {
            HolidayFile();
            Holidays.Choose("Holidays.kcal", _open, "Home.kcal");

            Assert.Equal([new DateTime(2026, 12, 18), new DateTime(2026, 12, day), new DateTime(2027, 1, 1)], Days(Fridays(shift)));
        }

        [Fact]
        public void KeepingTheDateLeavesItOnTheHoliday()
        {
            HolidayFile();
            Holidays.Choose("Holidays.kcal", _open, "Home.kcal");
            Assert.Contains(new DateTime(2026, 12, 25), Days(Fridays(0)));
        }

        [Fact]
        public void AWeekendDateMovesWithNoHolidayCalendar()
        {
            var monthly = new CalendarEvent
            {
                Title = "Rent", Start = new DateTime(2026, 7, 1), End = new DateTime(2026, 7, 2), AllDay = true,
                Repeat = RepeatFreq.Monthly, RepeatCount = 3, RepeatShift = 1,
            };
            var days = Recurrence.Expand(monthly, new DateTime(2026, 7, 1), new DateTime(2026, 10, 1)).Select(o => o.Start.Date).ToList();
            Assert.Equal([new DateTime(2026, 7, 1), new DateTime(2026, 8, 3), new DateTime(2026, 9, 1)], days);   // 1 August is a Saturday
        }

        [Fact]
        public void AMovedDateKeepsItsIdentityForSkipping()
        {
            HolidayFile();
            Holidays.Choose("Holidays.kcal", _open, "Home.kcal");
            _open.Add(Fridays(1));
            var moved = _open.GetInRange(new DateTime(2026, 12, 28), new DateTime(2026, 12, 29)).Single();
            Assert.Equal(new DateTime(2026, 12, 25, 9, 0, 0), moved.OccurrenceStart);

            _open.DeleteOccurrence(moved);

            Assert.Equal([new DateTime(2026, 12, 18), new DateTime(2027, 1, 1)],
                _open.GetInRange(new DateTime(2026, 12, 1), new DateTime(2027, 2, 1)).Select(o => o.Start.Date).ToList());
        }

        [Fact]
        public void TheOpenKillendarCanBeTheHolidayCalendar()
        {
            _open.Add(new CalendarEvent { Title = "Boxing Day", Start = new DateTime(2026, 12, 28), End = new DateTime(2026, 12, 29), AllDay = true });
            Holidays.Choose("Home.kcal", _open, "Home.kcal");
            Assert.Equal("Boxing Day", Holidays.NameOf(new DateTime(2026, 12, 28)));

            _open.Add(new CalendarEvent { Title = "New Year", Start = new DateTime(2027, 1, 1), End = new DateTime(2027, 1, 2), AllDay = true });
            Assert.Equal("New Year", Holidays.NameOf(new DateTime(2027, 1, 1)));
        }

        [Fact]
        public void AnEncryptedHolidayCalendarWaitsForItsPassword()
        {
            HolidayFile("secret");
            Holidays.Choose("Holidays.kcal", _open, "Home.kcal");
            Assert.Equal(Holidays.State.Locked, Holidays.Status);
            Assert.False(Holidays.IsHoliday(new DateTime(2026, 12, 25)));

            Assert.False(Holidays.Unlock("wrong"));
            Assert.True(Holidays.Unlock("secret"));
            Assert.True(Holidays.IsHoliday(new DateTime(2026, 12, 25)));
        }

        [Fact]
        public void CalendarFilesShowTheMovedDateAndKeepTheSetting()
        {
            HolidayFile();
            Holidays.Choose("Holidays.kcal", _open, "Home.kcal");
            var series = Fridays(1);
            series.Start = new DateTime(DateTime.Today.Year, 12, 18, 9, 0, 0);
            series.End = series.Start.AddHours(1);

            string text = IcsService.BuildIcs([series]);

            Assert.Contains("X-KILLENDAR-WORKDAY:NEXT", text);
            Assert.Contains("RECURRENCE-ID:" + series.Start.Year + "1225T090000", text);   // the holiday, moved
            Assert.Contains("DTSTART:" + new DateTime(series.Start.Year, 12, 28, 9, 0, 0).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"), text);
            var back = IcsService.ParseText(text).Events;
            Assert.Equal(1, back.Single(e => e.IsSeries).RepeatShift);
        }

        [Fact]
        public void TheEditorSavesTheChoice()
        {
            using var fixture = new AppointmentFixture();
            fixture.Editor.NewAt(new DateTime(2026, 12, 18, 9, 0, 0));
            fixture.View.FieldTitle = "Report";
            fixture.View.FieldRepeat = RepeatFreq.Weekly;
            fixture.View.FieldRepeatEveryN = 1;
            fixture.View.FieldRepeatDays = [DayOfWeek.Friday];
            fixture.View.FieldRepeatShift = -1;

            fixture.Editor.Save();

            Assert.Equal(-1, Assert.Single(fixture.Store.Events).RepeatShift);
        }
    }
}
