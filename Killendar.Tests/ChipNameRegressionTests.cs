using System;
using Killendar.Models;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    // VE-009: an untitled chip is named with the same placeholder the chip shows, so a screen
    // reader does not announce a bare ", 9:00 AM".
    public sealed class ChipNameRegressionTests
    {
        private static CalendarEvent Untitled(string title, string location = "") => new()
        {
            Title = title, Location = location,
            Start = new DateTime(2026, 11, 3, 9, 0, 0), End = new DateTime(2026, 11, 3, 10, 0, 0),
        };

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void UntitledChipIsNamedWithThePlaceholder(string title)
        {
            string name = CalendarChrome.ChipText(Untitled(title), "(No title)", ", ");
            Assert.StartsWith("(No title), ", name);
        }

        [Fact]
        public void UntitledTooltipStartsWithThePlaceholderLine()
        {
            string tip = CalendarChrome.ChipText(Untitled("", "Room 4"), "(No title)", "\n");
            Assert.StartsWith("(No title)\n", tip);
            Assert.EndsWith("\nRoom 4", tip);
        }

        [Fact]
        public void TitledChipKeepsItsTitle()
        {
            string name = CalendarChrome.ChipText(Untitled("Dentist", "Main St"), "(No title)", ", ");
            Assert.StartsWith("Dentist, ", name);
            Assert.EndsWith(", Main St", name);
        }
    }
}
