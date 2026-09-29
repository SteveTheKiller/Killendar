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
                var transfer = new IcsTransfer(host, store, () => refreshes++);

                transfer.Import(invite);

                Assert.Equal("Field visit", store.Events.Single().Title);
                Assert.Equal(1, refreshes);
                Assert.NotNull(host.Status);
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
            public string Loc(string key) => key == "Str_Status_Imported" ? "Imported {0}" : key;
            public void SetStatus(string text) => Status = text;
            public void ShowView(object view) { }
            public void HighlightTab(string which) { }
            public void ShowFineMonthNavigation(bool visible) { }
            public void StepDensity(int direction) { }
            public void ShowDayAgenda(DateTime day, CalendarEvent? highlight) { }
        }
    }
}
