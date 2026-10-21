using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using Killendar.Features;
using Killendar.Models;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A calendar host that answers Loc from the real English strings, so tests can
    /// assert the status text a user would read.</summary>
    internal sealed class EnglishCalendarHost : ICalendarHost
    {
        private static readonly Lazy<Dictionary<string, string>> Strings = new(() =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Strings", "en-US.xaml"))) root = root.Parent;
            Assert.NotNull(root);
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            return XDocument.Load(Path.Combine(root!.FullName, "Strings", "en-US.xaml")).Root!.Elements()
                .Where(e => e.Attribute(x + "Key") != null).ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Value);
        });

        public string Status { get; private set; } = "";
        public Window Window => null!;
        public string PeriodLabel { set { } }
        public string Loc(string key) => Strings.Value.TryGetValue(key, out var value) ? value : key;
        public void SetStatus(string text) => Status = text;
        public void ShowView(object view) { }
        public void HighlightTab(string which) { }
        public void ShowFineMonthNavigation(bool visible) { }
        public void StepDensity(int direction) { }
        public void ShowDayAgenda(DateTime day, CalendarEvent? highlight) { }
    }
}
