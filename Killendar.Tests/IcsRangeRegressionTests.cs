using System;
using System.Linq;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public class IcsRangeRegressionTests
    {
        [Theory]
        [InlineData("RANGE=THISANDFUTURE")]
        [InlineData("range=\"thisandfuture\"")]
        [InlineData("TZID=\"America/New_York\";RaNgE=\"ThIsAnDfUtUrE\"")]
        public void FutureRangeComponentIsReportedAndDroppedWhileOtherComponentsImport(string parameters)
        {
            string text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:range-master\r\nDTSTART:20261020T130000Z\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=4\r\nEND:VEVENT\r\n" +
                "BEGIN:VEVENT\r\nRECURRENCE-ID;" + parameters + ":20261021T130000\r\nDTSTART:20261021T160000Z\r\nDURATION:PT1H\r\nUID:range-master\r\nSUMMARY:Future edit\r\nEND:VEVENT\r\n" +
                "BEGIN:VEVENT\r\nUID:ordinary\r\nDTSTART:20261025T180000Z\r\nDURATION:PT1H\r\nSUMMARY:Ordinary\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

            var parsed = IcsService.ParseText(text);

            Assert.Equal(1, parsed.Unsupported);
            Assert.Equal(1, parsed.UnsupportedRanges);
            Assert.Equal(0, parsed.Unreadable);
            Assert.Equal(2, parsed.Events.Count);
            Assert.DoesNotContain(parsed.Events, e => e.IsOverride || e.Title == "Future edit");
            Assert.Single(parsed.Events, e => e.Title == "Ordinary");
            Assert.Equal(4, Recurrence.Expand(Assert.Single(parsed.Events, e => e.IsSeries),
                new DateTime(2026, 10, 19), new DateTime(2026, 10, 26)).Count());
        }
    }
}
