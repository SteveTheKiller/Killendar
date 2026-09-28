using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Killendar.Cli;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    public sealed class AgendaCliTests
    {
        private static string NewPath() => Path.Combine(Path.GetTempPath(), "KillendarCliTests", Guid.NewGuid().ToString("N"), "test.kcal");

        private static void Clean(string path)
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); }
            catch { /* Test cleanup is best effort. */ }
        }

        [Fact]
        public void AgendaExpandsRepeatsWithoutChangingDatabase()
        {
            string path = NewPath();
            try
            {
                var writer = new EventStore();
                writer.Open(path);
                writer.Add(new CalendarEvent
                {
                    Title = "Daily check",
                    Start = new DateTime(2026, 10, 1, 9, 0, 0),
                    End = new DateTime(2026, 10, 1, 10, 0, 0),
                    Repeat = RepeatFreq.Daily,
                    RepeatCount = 3,
                });
                writer.Close();
                byte[] before = File.ReadAllBytes(path);

                var events = Program.Agenda(path, new DateTime(2026, 10, 1), 4, 10);

                Assert.Equal(3, events.Length);
                Assert.All(events, item => Assert.Contains("Daily check", JsonSerializer.Serialize(item)));
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { Clean(path); }
        }

        [Fact]
        public void AgendaCannotReadEncryptedCalendarWithoutKey()
        {
            string path = NewPath();
            try
            {
                var writer = new EventStore();
                writer.Open(path);
                writer.SetPassword("secret");
                writer.Close();

                Assert.Throws<SqliteException>(() => Program.Agenda(path, new DateTime(2026, 10, 1), 1, 10));
            }
            finally { Clean(path); }
        }

        [Fact]
        public void CliProcessReturnsAgendaJson()
        {
            string path = NewPath();
            try
            {
                var writer = new EventStore();
                writer.Open(path);
                writer.Add(new CalendarEvent
                {
                    Title = "Site visit",
                    Start = new DateTime(2026, 10, 1, 9, 0, 0),
                    End = new DateTime(2026, 10, 1, 10, 0, 0),
                });
                writer.Close();
                string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Killendar.exe");
                var start = new ProcessStartInfo(executable, $"--cli agenda 2026-10-01 1 --database \"{path}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var process = Process.Start(start)!;
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                Assert.True(process.WaitForExit(10000), "Killendar CLI timed out");
                Assert.True(process.ExitCode == 0, error);
                using var json = JsonDocument.Parse(output);
                Assert.Equal("Site visit", json.RootElement.EnumerateArray().Single().GetProperty("title").GetString());
            }
            finally { Clean(path); }
        }

        [Fact]
        public void CreatingThroughOpenStorePersistsAndRefreshesItsMemory()
        {
            string path = NewPath();
            try
            {
                var store = new EventStore();
                store.Open(path);
                string response = CalendarCommands.Create(store,
                    "{\"title\":\"Site visit\",\"start\":\"2026-10-01T09:00\",\"end\":\"2026-10-01T10:00\"}");
                using var result = JsonDocument.Parse(response);
                Assert.False(result.RootElement.TryGetProperty("error", out _));
                Assert.Single(store.GetOnDay(new DateTime(2026, 10, 1)));
                store.Close();

                Assert.Single(Program.Agenda(path, new DateTime(2026, 10, 1), 1, 10));
            }
            finally { Clean(path); }
        }

        [Fact]
        public void CreatingRejectsInvalidOrLockedCalendar()
        {
            var closed = new EventStore();
            Assert.Contains("error", CalendarCommands.Create(closed,
                "{\"title\":\"Visit\",\"start\":\"2026-10-01T09:00\",\"end\":\"2026-10-01T10:00\"}"));

            string path = NewPath();
            try
            {
                closed.Open(path);
                Assert.Contains("error", CalendarCommands.Create(closed,
                    "{\"title\":\"Visit\",\"start\":\"2026-10-01T10:00\",\"end\":\"2026-10-01T09:00\"}"));
                Assert.Empty(closed.Events);
                closed.Close();
            }
            finally { Clean(path); }
        }
    }
}
