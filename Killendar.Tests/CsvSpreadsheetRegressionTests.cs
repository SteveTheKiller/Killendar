using System;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class CsvSpreadsheetRegressionTests
    {
        [Theory]
        [InlineData("=1+1")]
        [InlineData("+1+1")]
        [InlineData("-1+1")]
        [InlineData("@SUM(1)")]
        [InlineData("\t=1+1")]
        [InlineData("\r=1+1")]
        [InlineData(" \t+1+1")]
        [InlineData("\u0001=1+1")]
        [InlineData("\n=1+1")]
        [InlineData("=SUM(1,2)")]
        public void FormulaLikeTextIsProtectedInEveryTextColumn(string text)
        {
            var row = new CalendarEvent { Title = text, Description = text, Location = text, Categories = text,
                Start = new DateTime(2026, 10, 20, 9, 0, 0), End = new DateTime(2026, 10, 20, 10, 0, 0) };
            var imported = Assert.Single(CsvService.ParseText(CsvService.BuildCsv(new[] { row })).Events);
            Assert.True(CsvService.RequiresSpreadsheetProtection(new[] { row }));
            // Import removes the protecting apostrophe, so the text comes back as it was exported.
            string expected = text.Replace("\r", "").Trim();
            Assert.Equal(expected, imported.Title);
            Assert.Equal(expected, imported.Description);
            Assert.Equal(expected, imported.Location);
            Assert.Equal(EventStore.NormalizeCategories(expected), imported.Categories);
        }

        // KD150-R2: text the user typed with its own leading apostrophe before a formula sign
        // survives the round trip instead of losing that apostrophe on import.
        [Theory]
        [InlineData("'=ok")]
        [InlineData("''+1")]
        [InlineData("'-call")]
        public void TypedApostropheBeforeAFormulaSignSurvivesTheRoundTrip(string text)
        {
            var row = new CalendarEvent { Title = text, Start = new DateTime(2026, 10, 20, 9, 0, 0),
                End = new DateTime(2026, 10, 20, 10, 0, 0) };
            Assert.Equal(text, Assert.Single(CsvService.ParseText(CsvService.BuildCsv(new[] { row })).Events).Title);
        }

        [Theory]
        [InlineData("Ordinary appointment")]
        [InlineData("O'Brien")]
        [InlineData("+")]
        [InlineData("-")]
        public void OrdinaryTextAndLoneSignsRemainUnchanged(string text)
        {
            var row = new CalendarEvent { Title = text, Start = new DateTime(2026, 10, 20, 9, 0, 0),
                End = new DateTime(2026, 10, 20, 10, 0, 0) };
            Assert.False(CsvService.RequiresSpreadsheetProtection(new[] { row }));
            Assert.Equal(text, Assert.Single(CsvService.ParseText(CsvService.BuildCsv(new[] { row })).Events).Title);
        }
    }
}
