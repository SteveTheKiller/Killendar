using System;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public class IcsTextEscapeRegressionTests
    {
        [Fact]
        public void LiteralWindowsPathSurvivesCalendarRoundTrip()
        {
            AssertRoundTrip(@"C:\new\notes");
        }

        [Theory]
        [InlineData(@"literal\n")]
        [InlineData(@"literal\N")]
        [InlineData(@"double\\slash")]
        [InlineData("Alpha, Beta")]
        [InlineData("Alpha;Beta")]
        [InlineData("Alpha\nBeta")]
        [InlineData("C:\\new\\Notes; Alpha, Beta\nLine\\\\tail")]
        [InlineData(@"unknown\q trailing\")]
        public void EscapedTextSurvivesAllSupportedTextFields(string text)
        {
            AssertRoundTrip(text);
        }

        [Fact]
        public void UnknownEscapeAndTrailingSlashRemainLiteralOnImport()
        {
            var result = IcsService.ParseText("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20261020\r\nSUMMARY:unknown\\q trailing\\\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
            Assert.Equal(@"unknown\q trailing\", Assert.Single(result.Events).Title);
        }

        private static void AssertRoundTrip(string text)
        {
            var original = new CalendarEvent
            {
                Title = text,
                Description = text,
                Location = text,
                Categories = text,
                AllDay = true,
                Start = new DateTime(2026, 10, 20),
                End = new DateTime(2026, 10, 21)
            };
            var result = IcsService.ParseText(IcsService.BuildIcs(new[] { original }));
            Assert.Equal(0, result.Unreadable);
            var restored = Assert.Single(result.Events);
            Assert.Equal(text, restored.Title);
            Assert.Equal(text, restored.Description);
            Assert.Equal(text, restored.Location);
            Assert.Equal(text, restored.Categories);
        }
    }
}
