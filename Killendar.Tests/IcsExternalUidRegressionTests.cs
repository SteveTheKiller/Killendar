using System;
using System.Linq;
using System.Text.RegularExpressions;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-004: an imported UID is the meeting's identity in every other calendar. Export must hand
    // it back unchanged, after storage, reopen and local edits, or reimporting there duplicates it.
    [Collection("Calendar settings")]
    public sealed class IcsExternalUidRegressionTests
    {
        private const string Uid = "meeting-123@example.com";

        [Fact]
        public void PlainImportedEventKeepsItsUidThroughStorageEditAndExport()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.ImportEvents(IcsService.ParseText(Calendar(
                "UID:" + Uid, "SUMMARY:Planning", "DTSTART:20261022T090000Z", "DTEND:20261022T100000Z")).Events);
            Reopen(fixture);

            var edited = Assert.Single(fixture.Store.Events).Clone();
            edited.Title = "Planning, moved";
            fixture.Store.Update(edited);
            Reopen(fixture);

            Assert.Equal(new[] { Uid }, Uids(IcsService.BuildIcs(fixture.Store.Events)));
        }

        [Fact]
        public void SeriesAndOverrideShareTheImportedUidOnExport()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.ImportEvents(IcsService.ParseText(Calendar(
                "UID:" + Uid, "SUMMARY:Standup", "DTSTART:20261019T090000Z", "DTEND:20261019T091500Z",
                "RRULE:FREQ=DAILY;COUNT=5", "END:VEVENT", "BEGIN:VEVENT",
                "UID:" + Uid, "RECURRENCE-ID:20261021T090000Z", "SUMMARY:Standup, late",
                "DTSTART:20261021T100000Z", "DTEND:20261021T101500Z")).Events);
            Reopen(fixture);

            string ics = IcsService.BuildIcs(fixture.Store.Events);

            Assert.Equal(new[] { Uid, Uid }, Uids(ics));
            Assert.Contains("RECURRENCE-ID", ics);
        }

        [Fact]
        public void OccurrenceEditedLocallyKeepsTheSeriesUid()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.ImportEvents(IcsService.ParseText(Calendar(
                "UID:" + Uid, "SUMMARY:Standup", "DTSTART:20261019T090000Z", "DTEND:20261019T091500Z",
                "RRULE:FREQ=DAILY;COUNT=5")).Events);
            var occurrence = fixture.Store.GetOnDay(new DateTime(2026, 10, 21).ToLocalTime().Date)
                .Single(e => e.IsOccurrence).Clone();
            occurrence.Title = "Standup, just this one";
            fixture.Store.SaveOccurrence(occurrence);
            Reopen(fixture);

            Assert.Equal(new[] { Uid, Uid }, Uids(IcsService.BuildIcs(fixture.Store.Events)));
        }

        [Fact]
        public void NativeAppointmentsStillExportTheirOwnId()
        {
            using var fixture = new AppointmentFixture();
            var ev = new CalendarEvent { Title = "Local", Start = new DateTime(2026, 10, 22, 9, 0, 0),
                End = new DateTime(2026, 10, 22, 10, 0, 0) };
            fixture.Store.Add(ev);

            Assert.Equal(new[] { ev.Id.ToString() }, Uids(IcsService.BuildIcs(fixture.Store.Events)));
        }

        [Fact]
        public void ReimportingTheExportFindsTheSameAppointment()
        {
            using var fixture = new AppointmentFixture();
            var imported = IcsService.ParseText(Calendar(
                "UID:" + Uid, "SUMMARY:Planning", "DTSTART:20261022T090000Z", "DTEND:20261022T100000Z")).Events;
            fixture.Store.ImportEvents(imported);

            var again = IcsService.ParseText(IcsService.BuildIcs(fixture.Store.Events)).Events;

            Assert.Equal(0, fixture.Store.ImportEvents(again));
        }

        [Fact]
        public void CalendarFromBeforeSchemaEightOpensWithEmptyExternalUid()
        {
            using var fixture = new AppointmentFixture();
            var ev = new CalendarEvent { Title = "Old", Start = new DateTime(2026, 10, 22, 9, 0, 0),
                End = new DateTime(2026, 10, 22, 10, 0, 0) };
            fixture.Store.Add(ev);
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                db.Open();
                using var drop = db.CreateCommand();
                drop.CommandText = "ALTER TABLE events DROP COLUMN external_uid; UPDATE meta SET value='7' WHERE key='schema_version'";
                drop.ExecuteNonQuery();
            }

            fixture.Store.Open(path);

            Assert.Equal("", Assert.Single(fixture.Store.Events).ExternalUid);
            Assert.Equal(new[] { ev.Id.ToString() }, Uids(IcsService.BuildIcs(fixture.Store.Events)));
        }

        private static string[] Uids(string ics) =>
            Regex.Matches(ics.Replace("\r\n ", ""), @"^UID:(.*)\r?$", RegexOptions.Multiline)
                .Cast<Match>().Select(m => m.Groups[1].Value.TrimEnd('\r')).ToArray();

        private static void Reopen(AppointmentFixture fixture)
        {
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);
        }

        private static string Calendar(params string[] lines) =>
            "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n" + string.Join("\r\n", lines) + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    }
}
