using System;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class CsvIdentityRegressionTests
    {
        [Theory]
        [InlineData("description")]
        [InlineData("categories")]
        [InlineData("all-day")]
        public void DistinctStoredFieldsRemainDistinctAndRepeatedImportsDeduplicate(string difference)
        {
            var first = new CalendarEvent { Title = "Same title", Start = new DateTime(2026, 10, 20),
                End = new DateTime(2026, 10, 21), Description = "Customer One", Location = "Office", Categories = "Red" };
            var second = first.Clone();
            if (difference == "description") second.Description = "Customer Two";
            if (difference == "categories") second.Categories = "Blue";
            if (difference == "all-day") second.AllDay = true;
            string csv = CsvService.BuildCsv(new[] { first, second, first });
            var parsed = CsvService.ParseText(csv);
            using var fixture = new AppointmentFixture();

            Assert.Equal(2, parsed.Events.Select(e => e.Id).Distinct().Count());
            Assert.Equal(2, CsvService.ImportEvents(fixture.Store, parsed.Events));
            Assert.Equal(0, CsvService.ImportEvents(fixture.Store, CsvService.ParseText(csv).Events));
            Assert.Equal(2, fixture.Store.Events.Count);
        }

        [Fact]
        public void LegacyImportedRowIsKeptAndOnlyItsDistinctVariantIsAdded()
        {
            var row = new CalendarEvent { Title = "Legacy", Start = new DateTime(2026, 10, 20, 9, 0, 0),
                End = new DateTime(2026, 10, 20, 10, 0, 0), Location = "Office", Description = "Original", Categories = "Red" };
            string key = row.Title + "\n" + row.Start.Ticks.ToString(CultureInfo.InvariantCulture) + "\n" +
                row.End.Ticks.ToString(CultureInfo.InvariantCulture) + "\n" + row.Location;
            using (var md5 = MD5.Create()) row.Id = new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(key)));
            Guid originalId = row.Id;
            var variant = row.Clone();
            variant.Description = "Different";
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(row);
            string csv = CsvService.BuildCsv(new[] { row, variant });

            Assert.Equal(1, CsvService.ImportEvents(fixture.Store, CsvService.ParseText(csv).Events));
            Assert.Equal(0, CsvService.ImportEvents(fixture.Store, CsvService.ParseText(csv).Events));
            Assert.Equal(2, fixture.Store.Events.Count);
            Assert.Contains(fixture.Store.Events, e => e.Id == originalId && e.Description == "Original");
            Assert.Contains(fixture.Store.Events, e => e.Description == "Different");
        }
    }
}
