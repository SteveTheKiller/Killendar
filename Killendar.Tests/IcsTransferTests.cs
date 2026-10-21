using System;
using System.IO;
using System.Linq;
using System.Windows;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class IcsTransferTests
    {
        [Fact]
        public void ImportPathAddsIcsEventsAndRefreshesTheCalendar()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarIcsTests", Guid.NewGuid().ToString("N"));
            string database = Path.Combine(directory, "test.kcal");
            string invite = Path.Combine(directory, "invite.ics");
            Directory.CreateDirectory(directory);

            var store = new EventStore();
            try
            {
                IcsService.ExportToFile([new CalendarEvent
                {
                    Title = "Field visit",
                    Start = new DateTime(2026, 10, 2, 9, 0, 0),
                    End = new DateTime(2026, 10, 2, 10, 0, 0),
                }], invite);
                store.Open(database);
                var host = new CalendarHostStub();
                int refreshes = 0;
                store.Changed += () => refreshes++;
                var transfer = new IcsTransfer(host, store);

                transfer.Import(invite);

                Assert.Equal("Field visit", store.Events.Single().Title);
                Assert.Equal(1, refreshes);
                Assert.Equal("Imported 1", host.Status);

                transfer.Import(invite);
                Assert.Single(store.Events);
                Assert.Equal(1, refreshes);
                Assert.Equal("Imported 0, skipped 1", host.Status);

                store.Close();
                store.Open(database);
                var reopened = Assert.Single(store.Events);
                Assert.Equal("Field visit", reopened.Title);
                Assert.Equal(new DateTime(2026, 10, 2, 9, 0, 0), reopened.Start);
                Assert.Equal(new DateTime(2026, 10, 2, 10, 0, 0), reopened.End);
            }
            finally
            {
                store.Close();
                Directory.Delete(directory, true);
            }
        }

        private sealed class CalendarHostStub : ICalendarHost
        {
            public string? Status { get; private set; }
            public Window Window => null!;
            public string PeriodLabel { set { } }
            public string Loc(string key) => key switch
            {
                "Str_Status_Imported" => "Imported {0}",
                "Str_Status_ImportedSkipped" => "Imported {0}, skipped {1}",
                _ => key,
            };
            public void SetStatus(string text) => Status = text;
            public void ShowView(object view) { }
            public void HighlightTab(string which) { }
            public void ShowFineMonthNavigation(bool visible) { }
            public void StepDensity(int direction) { }
            public void ShowDayAgenda(DateTime day, CalendarEvent? highlight) { }
        }
    }
}
