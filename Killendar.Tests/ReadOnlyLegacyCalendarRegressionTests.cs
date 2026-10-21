using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class ReadOnlyLegacyCalendarRegressionTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void AgendaReadsPreviousSchemasWithoutChangingTheFile(int version)
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\readonly-legacy", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "calendar.kcal");
            var store = new EventStore();
            try
            {
                store.Open(path);
                store.Add(new CalendarEvent { Title = "Existing appointment", AllDay = true,
                    Start = new DateTime(2026, 10, 20), End = new DateTime(2026, 10, 21),
                    Repeat = RepeatFreq.Daily, RepeatCount = 3 });
                store.Close();
                using (var db = new SqliteConnection("Data Source=" + path))
                {
                    db.Open();
                    using var command = db.CreateCommand();
                    string[] columns = { "skip_occurrences", "repeat_until_exact", "occurrence_anchor", "occurrence_basis", "occurrence_zone_id" };
                    command.CommandText = string.Join(";", columns.Select(name => "ALTER TABLE events DROP COLUMN " + name));
                    if (version < 5) command.CommandText += ";ALTER TABLE events DROP COLUMN repeat_week_start";
                    if (version < 4) command.CommandText += ";ALTER TABLE events DROP COLUMN repeat_time_basis;ALTER TABLE events DROP COLUMN repeat_anchor;ALTER TABLE events DROP COLUMN repeat_zone_id";
                    command.CommandText += ";UPDATE meta SET value=$version WHERE key='schema_version'";
                    command.Parameters.AddWithValue("$version", version.ToString());
                    command.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();
                byte[] before = File.ReadAllBytes(path);
                var agenda = Killendar.Cli.Program.Agenda(path, new DateTime(2026, 10, 20), 4, 50);
                Assert.Equal(3, agenda.Length);
                store.OpenReadOnly(path);
                Assert.Equal("Existing appointment", Assert.Single(store.Events).Title);
                Assert.Equal(new[] { 20, 21, 22 }, store.GetInRange(new DateTime(2026, 10, 20), new DateTime(2026, 10, 24)).Select(item => item.Start.Day));
                store.Load();
                Assert.Null(store.LoadError);
                Assert.Single(store.Events);
                store.Close();
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Single(Directory.GetFiles(root));
            }
            finally { store.Close(); Directory.Delete(root, true); }
        }
    }
}
