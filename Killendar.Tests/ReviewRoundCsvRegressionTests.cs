using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class ReviewRoundCsvRegressionTests
    {
        [Fact]
        public void ProtectedTitleSurvivesExportAndReimportWithoutDuplicating()
        {
            using var fixture = new AppointmentFixture();
            var row = new CalendarEvent { Title = "-call", Start = new DateTime(2026, 10, 20, 9, 0, 0),
                End = new DateTime(2026, 10, 20, 10, 0, 0) };
            Assert.Equal(1, CsvService.ImportEvents(fixture.Store,
                CsvService.ParseText(CsvService.BuildCsv(new[] { row })).Events));
            string csv = CsvService.BuildCsv(fixture.Store.Events);

            var parsed = CsvService.ParseText(csv);

            Assert.Equal("-call", Assert.Single(parsed.Events).Title);
            Assert.Equal(0, CsvService.ImportEvents(fixture.Store, parsed.Events));
            Assert.Equal("-call", Assert.Single(fixture.Store.Events).Title);
        }
    }
}
