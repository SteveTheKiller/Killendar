using System;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S15: configurable workdays and hours, including weeks that are not Monday to Friday.
    [Collection("Calendar settings")]
    public sealed class WorkScheduleRegressionTests
    {
        [Fact]
        public void DefaultsToMondayToFridayNineToFive()
        {
            WorkSchedule.Load(null, null);
            Assert.True(WorkSchedule.IsWorkday(DayOfWeek.Monday));
            Assert.False(WorkSchedule.IsWorkday(DayOfWeek.Saturday));
            Assert.Equal(9, WorkSchedule.StartHour);
            Assert.Equal(17, WorkSchedule.EndHour);
        }

        [Fact]
        public void SundayToThursdayIsAWholeWorkWeek()
        {
            WorkSchedule.Load("0,1,2,3,4", "8-16");
            try
            {
                var (first, count) = WorkSchedule.WorkWeek(new DateTime(2026, 10, 21), DayOfWeek.Sunday);
                Assert.Equal(new DateTime(2026, 10, 18), first);
                Assert.Equal(5, count);
                Assert.True(WorkSchedule.IsWorkingHour(8));
                Assert.False(WorkSchedule.IsWorkingHour(16));
            }
            finally { WorkSchedule.Load(null, null); }
        }

        [Fact]
        public void GapsInsideTheWeekStayAsShadedColumns()
        {
            WorkSchedule.Load("1,3,5", null);
            try
            {
                var (first, count) = WorkSchedule.WorkWeek(new DateTime(2026, 10, 21), DayOfWeek.Monday);
                Assert.Equal(new DateTime(2026, 10, 19), first);
                Assert.Equal(5, count);   // Monday to Friday, Tuesday and Thursday shaded
                Assert.False(WorkSchedule.IsWorkday(DayOfWeek.Tuesday));
            }
            finally { WorkSchedule.Load(null, null); }
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("9,x", "17-9")]
        [InlineData("garbage", "25-30")]
        public void UnreadableSettingsKeepTheDefault(string days, string hours)
        {
            WorkSchedule.Load(days, hours);
            Assert.Equal(5, WorkSchedule.Days.Count);
            Assert.Equal(9, WorkSchedule.StartHour);
        }

        [Fact]
        public void SettingsRoundTrip()
        {
            Assert.Equal("0,2,4", WorkSchedule.FormatDays([DayOfWeek.Thursday, DayOfWeek.Sunday, DayOfWeek.Tuesday]));
            Assert.Equal("7-15", WorkSchedule.FormatHours(7, 15));
        }
    }
}
