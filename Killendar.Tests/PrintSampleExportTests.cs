using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Web pages for printing to PDF and looking at: every layout, a busy month, long titles and
    // places, all private details on. Writes an A4 and a Letter copy of each. Runs only when
    // KILLENDAR_PRINT_SAMPLES names a folder; otherwise it does nothing.
    [Collection("Calendar settings")]
    public sealed class PrintSampleExportTests
    {
        public PrintSampleExportTests() => System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));

        [Fact]
        public void WritePages()
        {
            string? folder = Environment.GetEnvironmentVariable("KILLENDAR_PRINT_SAMPLES");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            var rows = new List<CalendarEvent>();
            var random = new Random(7);
            var from = new DateTime(2026, 12, 1);
            string[] titles = ["Standup", "Dentist", "Quarterly business review with the regional leadership team and finance",
                               "Lunch", "Call with Tokyo office about the Q1 roadmap", "Gym", "Pick up the kids", "Board review"];
            string[] places = ["Main Street 4", "Conference Room B, 3rd floor, North Building, 1200 Corporate Parkway", "", "Online"];
            string[] categories = ["", "Red", "Blue", "Green", "Purple"];
            for (int day = 0; day < 31; day++)
            {
                int count = day % 7 == 2 ? 9 : random.Next(0, 4);   // one very busy day a week
                for (int i = 0; i < count; i++)
                {
                    var start = from.AddDays(day).AddHours(8 + random.Next(0, 10)).AddMinutes(15 * random.Next(0, 4));
                    rows.Add(new CalendarEvent
                    {
                        Title = titles[random.Next(titles.Length)], Location = places[random.Next(places.Length)],
                        Description = i % 3 == 0 ? "Bring the printed agenda.\nSecond line of notes, with <angle> & ampersand." : "",
                        Attendees = i % 4 == 0 ? ["pat@example.com", "sam.longname@example.org"] : [],
                        Categories = categories[random.Next(categories.Length)],
                        Start = start, End = start.AddMinutes(30 * random.Next(1, 5)),
                    });
                }
            }
            rows.Add(new CalendarEvent { Title = "Holiday trip to the mountains", Start = new DateTime(2026, 12, 21), End = new DateTime(2026, 12, 28), AllDay = true, Categories = "Green" });
            rows.Add(new CalendarEvent { Title = "Conference", Start = new DateTime(2026, 12, 9), End = new DateTime(2026, 12, 12), AllDay = true });

            foreach (HtmlLayout layout in Enum.GetValues(typeof(HtmlLayout)))
            {
                var options = new HtmlOptions { Layout = layout, Locations = true, Descriptions = true, Attendees = true };
                var to = layout == HtmlLayout.Day ? from.AddDays(7) : from.AddMonths(1);
                string html = HtmlExport.Build(rows, from, to, options);
                foreach (var paper in new[] { "A4", "Letter" })
                {
                    // The page's own print rules, with the paper size the PDF printer is to use.
                    string sized = html.Replace("</head>", "<style>@page { size: " + paper + "; }</style></head>");
                    File.WriteAllText(Path.Combine(folder, layout.ToString().ToLowerInvariant() + "-" + paper.ToLowerInvariant() + ".html"),
                        sized, new UTF8Encoding(false));
                }
            }
        }
    }
}
