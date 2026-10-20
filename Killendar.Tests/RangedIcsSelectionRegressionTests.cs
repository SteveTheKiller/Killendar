using System;
using System.IO;
using System.Linq;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class RangedIcsSelectionRegressionTests
    {
        [Fact]
        public void LegacyMovedOccurrenceCanExportUsingItsExistingMasterIdentity()
        {
            using var fixture = new AppointmentFixture();
            var monday = new DateTime(2026, 10, 19, 9, 0, 0);
            fixture.Store.Add(new CalendarEvent { Title = "Weekly", Start = monday,
                End = monday.AddHours(1), Repeat = RepeatFreq.Weekly, RepeatCount = 3 });
            var moved = fixture.Store.GetOnDay(monday.AddDays(7)).Single().Clone();
            moved.Start = moved.Start.AddDays(1);
            moved.End = moved.End.AddDays(1);
            fixture.Store.SaveOccurrence(moved);
            var legacy = Assert.Single(fixture.Store.Events, row => row.IsOverride).Clone();
            legacy.OccurrenceAnchor = null;
            legacy.OccurrenceBasis = OccurrenceTimeBasis.Date;
            legacy.OccurrenceZoneId = "";
            fixture.Store.Update(legacy);
            var outside = fixture.Store.GetOnDay(monday).Single().Clone();
            outside.Title = "Edited outside the selected range";
            fixture.Store.SaveOccurrence(outside);
            var transfer = new IcsTransfer(null!, fixture.Store, () => { });
            var selected = transfer.SelectForIcs(monday.AddDays(8).Date, monday.AddDays(9).Date);

            Assert.Null(Record.Exception(() => IcsService.BuildIcs(selected)));
            Assert.Contains(selected, row => row.IsSeries);
            Assert.Equal(2, selected.Count(row => row.IsOverride));
        }

        [Fact]
        public void MovedOccurrenceRangedArtifactPreservesItsVisibleAppointment()
        {
            using var fixture = new AppointmentFixture();
            var monday = new DateTime(2026, 10, 19, 9, 0, 0);
            fixture.Store.Add(new CalendarEvent { Title = "Weekly", Start = monday,
                End = monday.AddHours(1), Repeat = RepeatFreq.Weekly, RepeatCount = 3 });
            var moved = fixture.Store.GetOnDay(monday.AddDays(7)).Single().Clone();
            moved.Start = moved.Start.AddDays(1);
            moved.End = moved.End.AddDays(1);
            moved.Title = "Moved Tuesday";
            fixture.Store.SaveOccurrence(moved);
            var transfer = new IcsTransfer(null!, fixture.Store, () => { });
            var selected = transfer.SelectForIcs(monday.AddDays(8).Date, monday.AddDays(9).Date);
            string artifact = IcsService.BuildIcs(selected);
            string folder = @"C:\Users\steve\scratch\killendar-remediation\ranged-ics";
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "moved-tuesday.ics"), artifact);
            using var imported = new AppointmentFixture();
            imported.Store.ImportEvents(IcsService.ParseText(artifact).Events);
            Assert.Equal("Moved Tuesday", Assert.Single(imported.Store.GetOnDay(monday.AddDays(8))).Title);
            imported.Store.ImportEvents(IcsService.ParseText(artifact).Events);
            Assert.Equal(2, imported.Store.Events.Count);
            string roundTrip = IcsService.BuildIcs(imported.Store.Events);
            File.WriteAllText(Path.Combine(folder, "moved-tuesday-roundtrip.ics"), roundTrip);
            using var reopened = new AppointmentFixture();
            reopened.Store.ImportEvents(IcsService.ParseText(roundTrip).Events);
            var visible = Assert.Single(reopened.Store.GetOnDay(monday.AddDays(8)));
            Assert.Equal("Moved Tuesday", visible.Title);
            Assert.Equal(moved.Start, visible.Start);
            Assert.Equal(moved.OccurrenceStart, visible.OccurrenceStart);
        }
    }
}
