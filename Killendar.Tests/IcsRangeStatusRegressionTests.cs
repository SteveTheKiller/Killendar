using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public class IcsRangeStatusRegressionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ImportPathDescribesFutureRangeLossSeparatelyFromTasks(bool includeTask)
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\range-status", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new EventStore();
            try
            {
                string path = Path.Combine(root, "range.ics");
                File.WriteAllText(path, "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:future\r\nRECURRENCE-ID;RANGE=THISANDFUTURE:20261021T130000Z\r\nDTSTART:20261021T160000Z\r\nDURATION:PT1H\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nUID:valid\r\nSUMMARY:Valid\r\nDTSTART:20261022T160000Z\r\nDURATION:PT1H\r\nEND:VEVENT\r\n" +
                    (includeTask ? "BEGIN:VTODO\r\nSUMMARY:Task\r\nEND:VTODO\r\n" : "") + "END:VCALENDAR\r\n");
                store.Open(Path.Combine(root, "calendar.kcal"));
                var host = new Host();
                int refreshes = 0;
                store.Changed += () => refreshes++;
                new IcsTransfer(host, store).Import(path);

                Assert.Equal("Valid", Assert.Single(store.Events).Title);
                Assert.Equal(1, refreshes);
                Assert.Contains(". Unsupported edits affecting future occurrences were skipped: 1", host.Status);
                if (includeTask) Assert.Contains("1 were tasks or notes", host.Status);
                else Assert.DoesNotContain("tasks or notes", host.Status);
                Assert.DoesNotContain("2 were tasks or notes", host.Status);
            }
            finally
            {
                store.Close();
                Directory.Delete(root, true);
            }
        }

        private sealed class Host : ICalendarHost
        {
            private readonly Dictionary<string, string> _strings;
            internal Host()
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root != null && !File.Exists(Path.Combine(root.FullName, "Strings", "en-US.xaml"))) root = root.Parent;
                Assert.NotNull(root);
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                _strings = XDocument.Load(Path.Combine(root!.FullName, "Strings", "en-US.xaml")).Root!.Elements()
                    .Where(e => e.Attribute(x + "Key") != null).ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Value);
            }
            public string Status { get; private set; } = "";
            public Window Window => null!;
            public string PeriodLabel { set { } }
            public string Loc(string key) => _strings.TryGetValue(key, out var value) ? value : key;
            public void SetStatus(string text) => Status = text;
            public void ShowView(object view) { }
            public void HighlightTab(string which) { }
            public void ShowFineMonthNavigation(bool visible) { }
            public void StepDensity(int direction) { }
            public void ShowDayAgenda(DateTime day, CalendarEvent? highlight) { }
        }
    }
}
