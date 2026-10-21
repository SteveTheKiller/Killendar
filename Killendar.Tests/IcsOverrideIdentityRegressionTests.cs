using System;
using System.IO;
using System.Linq;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public class IcsOverrideIdentityRegressionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReimportingMasterAndEditedOccurrenceAddsNoDuplicate(bool allDay)
        {
            string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:identity-test-series\r\nSUMMARY:Master\r\nDTSTART:20260105T180000Z\r\nDTEND:20260105T190000Z\r\nRRULE:FREQ=DAILY;COUNT=3\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nRECURRENCE-ID:20260106T180000Z\r\nUID:identity-test-series\r\nSUMMARY:Edited\r\nDTSTART:20260106T200000Z\r\nDTEND:20260106T210000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            if (allDay)
                text = text.Replace("DTSTART:20260105T180000Z", "DTSTART;VALUE=DATE:20260105")
                    .Replace("DTEND:20260105T190000Z", "DTEND;VALUE=DATE:20260106")
                    .Replace("RECURRENCE-ID:20260106T180000Z", "RECURRENCE-ID;VALUE=DATE:20260106")
                    .Replace("DTSTART:20260106T200000Z", "DTSTART;VALUE=DATE:20260106")
                    .Replace("DTEND:20260106T210000Z", "DTEND;VALUE=DATE:20260107");
            string root = Path.Combine(TestScratch.Root + @"\override-identity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                store.Open(Path.Combine(root, "calendar.kcal"));
                var first = IcsService.ParseText(text);
                Assert.Equal(0, first.Unreadable);
                Assert.Equal(2, store.ImportEvents(first.Events));
                Assert.Equal(0, store.ImportEvents(IcsService.ParseText(text).Events));
                DateTime firstDate = first.Events.Single(e => !e.IsOverride).Start.Date;
                Assert.Equal("Master", Assert.Single(store.GetOnDay(firstDate)).Title);
                Assert.Equal("Edited", Assert.Single(store.GetOnDay(firstDate.AddDays(1))).Title);
                Assert.Equal("Master", Assert.Single(store.GetOnDay(firstDate.AddDays(2))).Title);
            }
            finally
            {
                store.Close();
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void OverrideIdentityUsesNormalizedOriginalOccurrenceAndFinalSeriesUid()
        {
            const string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nRECURRENCE-ID:20260106T180000Z\r\nDTSTART:20260106T200000Z\r\nDTEND:20260106T210000Z\r\nUID:identity-test-series\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
            var original = Assert.Single(IcsService.ParseText(text).Events);
            var equivalent = Assert.Single(IcsService.ParseText(text.Replace("RECURRENCE-ID:20260106T180000Z",
                "RECURRENCE-ID;TZID=America/New_York:20260106T130000")).Events);
            Assert.Equal(original.Id, equivalent.Id);
            Assert.Equal(original.SeriesId, equivalent.SeriesId);
            Assert.Equal(original.Id, Assert.Single(IcsService.ParseText(text.Replace("DTSTART:20260106T200000Z", "DTSTART:20260106T203000Z")).Events).Id);
            Assert.NotEqual(original.Id, Assert.Single(IcsService.ParseText(text.Replace("UID:identity-test-series", "UID:another-series")).Events).Id);
            Assert.NotEqual(original.Id, Assert.Single(IcsService.ParseText(text.Replace("RECURRENCE-ID:20260106T180000Z", "RECURRENCE-ID:20260107T180000Z")).Events).Id);
        }
    }
}
