using System;
using System.Linq;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class WholeCalendarExportRegressionTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(9999, 12)]
        public void HtmlExportRendersBoundaryMonthsWithoutDateOverflow(int year, int month)
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
            var start = new DateTime(year, month, 1);
            var end = year == 9999 ? DateTime.MaxValue : start.AddMonths(1);
            string? html = null;

            Assert.Null(Record.Exception(() => html = HtmlExport.Build(new System.Collections.Generic.List<CalendarEvent>(), start, end)));

            Assert.Contains("<div class=\"dn\">31</div>", html!);
            Assert.Contains(DateFormatManager.Format(new DateTime(year, month, 31)), html!);
        }

        [Fact]
        public void ExcessiveFlattenedSeriesRequiresAnExplicitRange()
        {
            using var fixture = new AppointmentFixture();
            var start = new DateTime(2026, 1, 5, 9, 0, 0);
            fixture.Store.Add(new CalendarEvent { Title = "Large", Start = start, End = start.AddHours(1),
                Repeat = RepeatFreq.Daily, RepeatCount = 50001 });
            var transfer = new IcsTransfer(null!, fixture.Store);

            var error = Assert.Throws<InvalidOperationException>(() => transfer.WholeCalendarSpan(out _, out _));

            Assert.Contains("date range", error.Message);
            Assert.Single(fixture.Store.Events);
        }

        [Fact]
        public void FutureUnboundedSeriesHasAtLeastItsFirstYearInTheExportSpan()
        {
            using var fixture = new AppointmentFixture();
            var start = DateTime.Today.AddYears(2).AddHours(9);
            fixture.Store.Add(new CalendarEvent { Title = "Future", Start = start, End = start.AddHours(1),
                Repeat = RepeatFreq.Daily });
            var transfer = new IcsTransfer(null!, fixture.Store);

            transfer.WholeCalendarSpan(out var from, out var to);

            Assert.True(to > start.AddYears(1));
            Assert.NotEmpty(fixture.Store.GetInRange(from, to));
        }

        [Fact]
        public void BoundedYearlySeriesIncludesAllOneHundredFiftyOccurrences()
        {
            using var fixture = new AppointmentFixture();
            var start = new DateTime(2026, 1, 5, 9, 0, 0);
            fixture.Store.Add(new CalendarEvent { Title = "Long", Start = start, End = start.AddHours(1),
                Repeat = RepeatFreq.Yearly, RepeatCount = 150 });
            var transfer = new IcsTransfer(null!, fixture.Store);

            transfer.WholeCalendarSpan(out var from, out var to);

            var rows = fixture.Store.GetInRange(from, to);
            Assert.Equal(150, rows.Count);
            Assert.Equal(start.AddYears(149), rows.Last().Start);
        }

        [Fact]
        public void LastSupportedDayUsesARepresentableExclusiveBoundary()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Last", Start = new DateTime(9999, 12, 31, 9, 0, 0),
                End = new DateTime(9999, 12, 31, 10, 0, 0) });
            var transfer = new IcsTransfer(null!, fixture.Store);
            DateTime from = default, to = default;

            Assert.Null(Record.Exception(() => transfer.WholeCalendarSpan(out from, out to)));

            Assert.Equal(DateTime.MaxValue, to);
            Assert.Single(fixture.Store.GetInRange(from, to));
        }
    }
}
