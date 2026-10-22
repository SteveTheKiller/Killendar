using System;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S18: diagnostics carry counts and choices, never what is in the calendar or where it lives.
    [Collection("Calendar settings")]
    public sealed class DiagnosticsRegressionTests
    {
        // Register WPF's pack URI scheme without creating an Application, as the export tests do.
        public DiagnosticsRegressionTests() => System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));

        [Fact]
        public void DiagnosticsCarryNoAppointmentContentOrPaths()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent
            {
                Title = "Secret surgery", Location = "12 Hidden Lane", Description = "https://meet.example.com/abc?pw=hunter2",
                Attendees = ["dr.who@example.com"], Categories = "Private",
                Start = new DateTime(2026, 10, 20, 9, 0, 0), End = new DateTime(2026, 10, 20, 10, 0, 0), ReminderMinutes = 15,
            });

            string text = Diagnostics.Build(fixture.Store);

            Assert.Contains("Appointments 1", text);
            Assert.Contains("with a reminder 1", text);
            foreach (string leak in new[] { "Secret", "Hidden", "meet.example.com", "hunter2", "dr.who", "Private",
                                            fixture.Store.FilePath, TestScratch.Root, Environment.UserName + "\\" })
                Assert.DoesNotContain(leak, text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AClosedCalendarIsDescribedWithoutItsError()
        {
            var store = new EventStore();
            string text = Diagnostics.Build(store);
            Assert.Contains("Killendar: not open", text);
        }
    }
}
