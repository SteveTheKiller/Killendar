using System;
using System.IO;
using System.Linq;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public class GeneratedRecurrenceGapRegressionTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(20)]
        public void GeneratedGapIsSkippedWithoutConsumingCountAcrossRangesAndOverrides(int count)
        {
            string root = Path.Combine(TestScratch.Root + @"\generated-gap", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                store.Open(Path.Combine(root, "gap.kcal"));
                var parsed = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:generated-gap\r\nDTSTART;TZID=America/New_York:20260307T023000\r\nDURATION:PT1H\r\nRRULE:FREQ=DAILY;COUNT=" + count + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
                Assert.Equal(1, store.ImportEvents(parsed.Events));
                var from = new DateTime(2026, 3, 6);
                var to = new DateTime(2026, 4, 1);
                var expected = Enumerable.Range(0, count).Select(n => new DateTime(2026, 3, 7, n == 0 ? 7 : 6, 30, 0, DateTimeKind.Utc).AddDays(n == 0 ? 0 : n + 1)).ToArray();
                var lastStart = expected.Last().ToLocalTime();
                var rows = store.GetInRange(from, to);
                Assert.Equal(expected, rows.Select(e => e.Start.ToUniversalTime()).ToArray());
                var last = Assert.Single(store.GetInRange(lastStart.AddMinutes(-1), lastStart.AddMinutes(1))).Clone();
                Assert.True(Recurrence.GeneratesDate(Assert.Single(store.Events), last.Start));
                last.Title = "Last valid occurrence";
                store.SaveOccurrence(last);
                Assert.Null(store.LoadError);
                store.Load();
                Assert.Equal("Last valid occurrence", Assert.Single(store.GetInRange(lastStart.AddMinutes(-1), lastStart.AddMinutes(1))).Title);
                var edited = store.GetInRange(from, to)[1].Clone();
                edited.Title = "Series title";
                store.UpdateSeries(edited);
                Assert.Null(store.LoadError);
                Assert.Equal(expected, store.GetInRange(from, to).Select(e => e.Start.ToUniversalTime()).ToArray());
                Assert.Empty(store.GetInRange(new DateTime(2026, 4, 1), new DateTime(2026, 4, 2)));
            }
            finally
            {
                store.Close();
                Directory.Delete(root, true);
            }
        }
    }
}
