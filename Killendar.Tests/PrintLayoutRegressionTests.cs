using System;
using System.Collections.Generic;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S16: the web page export prints Month, Week, Day or Agenda, includes private details only
    // when asked, and keys its category colors.
    [Collection("Calendar settings")]
    public sealed class PrintLayoutRegressionTests
    {
        private static readonly DateTime From = new(2026, 10, 19), To = new(2026, 10, 26);

        // Register WPF's pack URI scheme without creating an Application, as the other export tests do.
        public PrintLayoutRegressionTests() => System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));

        private static List<CalendarEvent> Rows() =>
        [
            new() { Title = "Dentist", Start = new DateTime(2026, 10, 20, 9, 0, 0), End = new DateTime(2026, 10, 20, 10, 0, 0),
                    Location = "Main Street 4", Description = "Bring <forms> & card", Attendees = ["sam@example.com"], Categories = "Red" },
            new() { Title = "Holiday", Start = new DateTime(2026, 10, 23), End = new DateTime(2026, 10, 24), AllDay = true },
        ];

        [Theory]
        [InlineData(HtmlLayout.Month)]
        [InlineData(HtmlLayout.Week)]
        [InlineData(HtmlLayout.Day)]
        [InlineData(HtmlLayout.Agenda)]
        public void EveryLayoutShowsTheAppointments(HtmlLayout layout)
        {
            string page = HtmlExport.Build(Rows(), From, To, new HtmlOptions { Layout = layout });
            Assert.Contains("Dentist", page);
            Assert.Contains("Holiday", page);
            Assert.Contains("class=\"layout-" + layout.ToString().ToLowerInvariant() + "\"", page);
        }

        [Fact]
        public void PrivateDetailsStayOutUnlessChosen()
        {
            string page = HtmlExport.Build(Rows(), From, To, new HtmlOptions { Layout = HtmlLayout.Agenda });
            Assert.Contains("Main Street 4", page);           // locations are on by default
            Assert.DoesNotContain("forms", page);
            Assert.DoesNotContain("sam@example.com", page);

            page = HtmlExport.Build(Rows(), From, To, new HtmlOptions
                { Layout = HtmlLayout.Agenda, Locations = false, Descriptions = true, Attendees = true });
            Assert.DoesNotContain("Main Street 4", page);
            Assert.Contains("Bring &lt;forms&gt; &amp; card", page);
            Assert.Contains("sam@example.com", page);
        }

        [Fact]
        public void AgendaListsOnlyDaysWithAppointmentsAndDayListsEveryDay()
        {
            string agenda = HtmlExport.Build(Rows(), From, To, new HtmlOptions { Layout = HtmlLayout.Agenda });
            Assert.Equal(2, Count(agenda, "<section class=\"day\">"));
            string day = HtmlExport.Build(Rows(), From, To, new HtmlOptions { Layout = HtmlLayout.Day });
            Assert.Equal(7, Count(day, "<section class=\"day\">"));
        }

        [Fact]
        public void LegendKeysTheCategoriesUsed()
        {
            string page = HtmlExport.Build(Rows(), From, To, new HtmlOptions { Layout = HtmlLayout.Week });
            Assert.Contains("class=\"legend\"", page);
            Assert.Contains(">Red<", page);
        }

        [Fact]
        public void OptionsRoundTripThroughSettings()
        {
            var options = new HtmlOptions { Layout = HtmlLayout.Week, Locations = false, Descriptions = true, Attendees = true };
            var back = HtmlOptions.Parse(options.ToSetting());
            Assert.Equal(HtmlLayout.Week, back.Layout);
            Assert.False(back.Locations);
            Assert.True(back.Descriptions);
            Assert.True(back.Attendees);
            Assert.Equal(HtmlLayout.Month, HtmlOptions.Parse(null).Layout);
        }

        private static int Count(string text, string part)
        {
            int n = 0;
            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
