using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-012 and G08: imports are read and written away from the window, can be canceled without
    // writing anything, refuse oversized input before reading it, and ask first when something in
    // the file will be skipped or lost.
    [Collection("Calendar settings")]
    public sealed class ImportPipelineRegressionTests
    {
        [Fact]
        public void CanceledWriteLeavesTheCalendarUntouched()
        {
            using var fixture = new AppointmentFixture();
            var plan = fixture.Store.PlanImport(Appointments(50));
            int changes = 0;
            fixture.Store.Changed += () => changes++;
            using var cancel = new CancellationTokenSource();
            int written = 0;

            Assert.Throws<OperationCanceledException>(() => fixture.Store.WriteImport(plan, cancel.Token,
                () => { if (++written == 20) cancel.Cancel(); }));

            Assert.Empty(fixture.Store.Events);
            Assert.Equal(0, changes);
            Reopen(fixture);
            Assert.Empty(fixture.Store.Events);
        }

        [Fact]
        public void PlanSeparatesNewFromAlreadyPresent()
        {
            using var fixture = new AppointmentFixture();
            var events = Appointments(5);
            fixture.Store.ImportEvents(events.Take(2));

            var plan = fixture.Store.PlanImport(Appointments(5));

            Assert.Equal(3, plan.Fresh.Count);
            Assert.Equal(2, plan.AlreadyPresent);
        }

        [Fact]
        public void WriteThenPublishAddsOnceAndPersists()
        {
            using var fixture = new AppointmentFixture();
            var plan = fixture.Store.PlanImport(Appointments(5));
            int changes = 0;
            fixture.Store.Changed += () => changes++;

            fixture.Store.WriteImport(plan, CancellationToken.None);
            Assert.Empty(fixture.Store.Events);
            Assert.Equal(5, fixture.Store.PublishImport(plan));

            Assert.Equal(1, changes);
            Assert.Equal(5, fixture.Store.Events.Count);
            Reopen(fixture);
            Assert.Equal(5, fixture.Store.Events.Count);
        }

        [Fact]
        public void CleanImportDoesNotAsk()
        {
            using var fixture = new AppointmentFixture();
            var host = new EnglishCalendarHost();

            new IcsTransfer(host, fixture.Store).Import(WriteIcs(fixture, Appointments(3)));

            Assert.Null(host.Confirmation);
            Assert.Equal(3, fixture.Store.Events.Count);
            Assert.Equal("Imported 3 appointments", host.Status);
        }

        [Fact]
        public void LossyImportAsksFirstAndDecliningAddsNothing()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.ImportEvents(Appointments(1));
            string file = WriteIcs(fixture, Appointments(3));
            var host = new EnglishCalendarHost { ConfirmAnswer = false };

            new IcsTransfer(host, fixture.Store).Import(file);

            Assert.Equal("Import 2 appointments?", host.Confirmation!.Value.Heading);
            Assert.Contains("Already in this Killendar, will be skipped: 1", host.Confirmation!.Value.Detail);
            Assert.Single(fixture.Store.Events);
            Assert.Equal("Import canceled. Nothing was added.", host.Status);
        }

        [Fact]
        public void AcceptedPreviewImportsTheNewAppointments()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.ImportEvents(Appointments(1));
            var host = new EnglishCalendarHost();

            new IcsTransfer(host, fixture.Store).Import(WriteIcs(fixture, Appointments(3)));

            Assert.NotNull(host.Confirmation);
            Assert.Equal(3, fixture.Store.Events.Count);
            Assert.Equal("Imported 2 appointments, skipped 1 already present", host.Status);
        }

        [Fact]
        public void CancelingWhileSavingAddsNothing()
        {
            using var fixture = new AppointmentFixture();
            var host = new EnglishCalendarHost { CancelWhile = "Adding" };

            // Large enough that the write is still running when the card's Cancel arrives; the
            // write also checks for cancellation right before it commits.
            new IcsTransfer(host, fixture.Store).Import(WriteIcs(fixture, Appointments(3000)));

            Assert.Empty(fixture.Store.Events);
            Assert.Equal("Import canceled. Nothing was added.", host.Status);
            Reopen(fixture);
            Assert.Empty(fixture.Store.Events);
        }

        [Fact]
        public void OversizedFileIsRefusedBeforeReading()
        {
            using var fixture = new AppointmentFixture();
            string file = WriteIcs(fixture, Appointments(3));
            var host = new EnglishCalendarHost();
            long saved = ImportLimits.MaxBytes;
            try
            {
                ImportLimits.MaxBytes = new FileInfo(file).Length - 1;
                new IcsTransfer(host, fixture.Store).Import(file);
            }
            finally { ImportLimits.MaxBytes = saved; }

            Assert.Empty(fixture.Store.Events);
            Assert.StartsWith("This file is too large or too complex to import.", host.Status);
        }

        [Fact]
        public void TooManyAppointmentsAreRefusedWhole()
        {
            int saved = ImportLimits.MaxEvents;
            try
            {
                ImportLimits.MaxEvents = 2;
                var text = File.ReadAllText(WriteIcsTo(Path.GetTempFileName(), Appointments(3)));
                Assert.Throws<ImportLimitException>(() => IcsService.ParseText(text));
            }
            finally { ImportLimits.MaxEvents = saved; }
        }

        [Fact]
        public void ParsingStopsWhenCanceled()
        {
            var text = File.ReadAllText(WriteIcsTo(Path.GetTempFileName(), Appointments(3)));
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            Assert.Throws<OperationCanceledException>(() => IcsService.ParseText(text, cancel.Token));
        }

        [Fact]
        public void CsvParsingStopsWhenCanceled()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            Assert.Throws<OperationCanceledException>(() => CsvService.ParseText(
                "Subject,Start Date\r\nA,2026-10-22\r\n", cancel.Token));
        }

        [Fact]
        public void CsvDuplicateCheckUsesTheCalendarAsItIsWhenReadingEnds()
        {
            using var fixture = new AppointmentFixture();
            string file = Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, "rows.csv");
            File.WriteAllText(file, "Subject,Start Date,Start Time,End Date,End Time,All day event\r\n" +
                "Row,2026-10-22,09:00,2026-10-22,10:00,FALSE\r\n");
            // A command-line create lands while the file is being read; the import still sees it.
            var host = new EnglishCalendarHost();
            host.WhileWaiting = () => fixture.Store.ImportEvents(CsvService.ParseFile(file).Events);

            new IcsTransfer(host, fixture.Store).Import(file);

            Assert.Single(fixture.Store.Events);
            Assert.Equal("Imported 0 appointments, skipped 1 already present", host.Status);
        }

        [Fact]
        public void DeeplyNestedEmailIsRefused()
        {
            string mail = "Content-Type: text/calendar\n\nBEGIN:VCALENDAR\nEND:VCALENDAR\n";
            for (int depth = ImportLimits.MaxMimeDepth + 2; depth > 0; depth--)
                mail = "Content-Type: multipart/mixed; boundary=\"b" + depth + "\"\n\n--b" + depth + "\n" +
                       mail + "--b" + depth + "--\n";
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, mail);
                Assert.Throws<ImportLimitException>(() => EmlService.ExtractCalendarText(path));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void CommandRequestsAreRefusedWhileAnImportIsWriting()
        {
            using var fixture = new AppointmentFixture();
            var plan = fixture.Store.PlanImport(Appointments(5));
            string? response = null;

            fixture.Store.WriteImport(plan, CancellationToken.None, () =>
                response ??= CalendarCommands.Create(fixture.Store,
                    "{\"title\":\"From CLI\",\"start\":\"2026-11-01T09:00\",\"end\":\"2026-11-01T10:00\"}"));

            Assert.Contains("busy", response);
            Assert.Empty(fixture.Store.Events);
        }

        private static CalendarEvent[] Appointments(int count) => Enumerable.Range(0, count).Select(i => new CalendarEvent
        {
            Id = new Guid(i + 1, 0, 0, new byte[8]),
            Title = "Imported " + i,
            Start = new DateTime(2026, 10, 1, 9, 0, 0).AddDays(i),
            End = new DateTime(2026, 10, 1, 10, 0, 0).AddDays(i),
        }).ToArray();

        private static string WriteIcs(AppointmentFixture fixture, CalendarEvent[] events) =>
            WriteIcsTo(Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, Guid.NewGuid().ToString("N") + ".ics"), events);

        private static string WriteIcsTo(string path, CalendarEvent[] events)
        {
            File.WriteAllText(path, IcsService.BuildIcs(events));
            return path;
        }

        private static void Reopen(AppointmentFixture fixture)
        {
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);
        }
    }
}
