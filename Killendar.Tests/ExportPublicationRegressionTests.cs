using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-008: an export over an existing file must never leave it truncated or half written.
    [Collection("Calendar settings")]
    public sealed class ExportPublicationRegressionTests : IDisposable
    {
        private static readonly byte[] Previous = Encoding.UTF8.GetBytes("previous export bytes");
        private readonly string _root = Path.Combine(TestScratch.Root, "export-publication", Guid.NewGuid().ToString("N"));

        public ExportPublicationRegressionTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            FilePublication.AfterStaged = null;
            Directory.Delete(_root, true);
        }

        public static IEnumerable<object[]> Formats() =>
            new[] { "ics", "csv", "html" }.Select(format => new object[] { format });

        [Theory]
        [MemberData(nameof(Formats))]
        public void FailureAfterStagingPreservesThePreviousExport(string format)
        {
            string path = Existing("calendar." + format);
            FilePublication.AfterStaged = _ => throw new IOException("injected publication failure");

            Assert.Throws<IOException>(() => Export(format, path));

            Assert.Equal(Previous, File.ReadAllBytes(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void SuccessfulExportReplacesThePreviousFile(string format)
        {
            string path = Existing("calendar." + format);

            Export(format, path);

            Assert.NotEqual(Previous, File.ReadAllBytes(path));
            Assert.Contains("Quarterly review", File.ReadAllText(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        }

        [Fact]
        public void FailureWhileWritingPreservesThePreviousFile()
        {
            string path = Existing("calendar.ics");

            Assert.Throws<IOException>(() => FilePublication.Write(path, stream =>
            {
                stream.Write(new byte[4096], 0, 4096);
                throw new IOException("injected disk full");
            }));

            Assert.Equal(Previous, File.ReadAllBytes(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        }

        [Fact]
        public void LockedDestinationKeepsItsBytesAndLeavesNoStage()
        {
            string path = Existing("calendar.ics");
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => Export("ics", path));

            Assert.Equal(Previous, File.ReadAllBytes(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        }

        [Fact]
        public void NewDestinationIsCreated()
        {
            string path = Path.Combine(_root, "new.ics");

            Export("ics", path);

            Assert.StartsWith("BEGIN:VCALENDAR", File.ReadAllText(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        }

        private string Existing(string name)
        {
            string path = Path.Combine(_root, name);
            File.WriteAllBytes(path, Previous);
            return path;
        }

        private static void Export(string format, string path)
        {
            var events = new List<CalendarEvent>
            {
                new CalendarEvent
                {
                    Title = "Quarterly review",
                    Start = new DateTime(2026, 10, 22, 9, 0, 0),
                    End = new DateTime(2026, 10, 22, 10, 0, 0)
                }
            };
            switch (format)
            {
                case "ics": IcsService.ExportToFile(events, path); break;
                case "csv": CsvService.ExportToFile(events, path); break;
                default:
                    // Register WPF's pack URI scheme without creating an Application or theme resources.
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
                    HtmlExport.ExportToFile(events, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1), path); break;
            }
        }
    }
}
