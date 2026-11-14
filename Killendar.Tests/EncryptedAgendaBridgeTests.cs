using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class EncryptedAgendaBridgeTests
    {
        [Fact]
        public async Task BusyCalendarPipeCanFinishItsCurrentRequest()
        {
            string name = "Killendar-busy-test-" + Guid.NewGuid().ToString("N");
            using var occupied = CalendarCommandPipe.CreateServer(name);
            using var occupant = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            var connected = occupied.WaitForConnectionAsync(); occupant.Connect(1000); await connected;
            using var stop = new System.Threading.CancellationTokenSource();
            NamedPipeServerStream? next = null;
            var served = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, stop.Token); occupied.Dispose();
                    next = CalendarCommandPipe.CreateServer(name); await next.WaitForConnectionAsync();
                    CalendarCommandPipe.Serve(next, _ => "[{\"title\":\"Busy pipe fixture\"}]");
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            });
            var previousOut = Console.Out; var previousError = Console.Error;
            using var output = new StringWriter(); using var error = new StringWriter();
            Console.SetOut(output); Console.SetError(error);
            try
            {
                int exit = Cli.Program.Run(new[] { "agenda", "2026-11-13", "1" }, name);
                Assert.True(exit == 0, "Busy calendar was reported closed: " + error);
                Assert.Contains("Busy pipe fixture", output.ToString());
            }
            finally { Console.SetOut(previousOut); Console.SetError(previousError); stop.Cancel(); occupied.Dispose(); next?.Dispose(); await served; }
        }

        [Fact]
        public void ClosedCalendarFailsPromptly()
        {
            var previous = Console.Error;
            using var error = new StringWriter();
            Console.SetError(error);
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Assert.Equal(1, Cli.Program.Run(new[] { "agenda", "2026-11-13", "1" }, "Killendar-closed-test-" + Guid.NewGuid().ToString("N")));
                Assert.True(clock.ElapsedMilliseconds < 200, "Closed calendar took " + clock.ElapsedMilliseconds + " ms");
                Assert.Contains("Open and unlock Killendar", error.ToString());
            }
            finally { Console.SetError(previous); }
        }

        [Fact]
        public async Task DefaultCliAgendaUsesTheRunningAppInsteadOfTheDatabaseFile()
        {
            string name = "Killendar-cli-test-" + Guid.NewGuid().ToString("N");
            using var server = CalendarCommandPipe.CreateServer(name);
            var served = Task.Run(async () =>
            {
                try { await server.WaitForConnectionAsync(); CalendarCommandPipe.Serve(server, _ => "[{\"title\":\"Pipe agenda fixture\"}]"); }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            });
            var previousOut = Console.Out;
            var previousError = Console.Error;
            using var output = new StringWriter();
            using var error = new StringWriter();
            Console.SetOut(output); Console.SetError(error);
            try
            {
                int exit = Cli.Program.Run(new[] { "agenda", "2026-11-13", "1" }, name);
                Assert.True(exit == 0, error.ToString());
                Assert.Contains("Pipe agenda fixture", output.ToString());
            }
            finally { Console.SetOut(previousOut); Console.SetError(previousError); server.Dispose(); await served; }
        }

        [Fact]
        public async Task AgendaReadsTheUnlockedEncryptedCalendarThroughThePipe()
        {
            string root = Path.Combine(Path.GetTempPath(), "killendar-agenda-bridge-" + Guid.NewGuid().ToString("N"));
            var store = new EventStore();
            string name = "Killendar-agenda-test-" + Guid.NewGuid().ToString("N");
            try
            {
                store.Open(Path.Combine(root, "fixture.kcal"));
                store.Add(new CalendarEvent { Title = "Encrypted agenda fixture", Start = new DateTime(2026, 11, 13), End = new DateTime(2026, 11, 14), AllDay = true });
                store.SetPassword("isolated fixture password");
                using var server = CalendarCommandPipe.CreateServer(name);
                var served = Task.Run(async () => { await server.WaitForConnectionAsync(); CalendarCommandPipe.Serve(server, request => CalendarCommands.Create(store, request)); });
                string response = CalendarCommandPipe.Send("{\"command\":\"agenda\",\"date\":\"2026-11-13\",\"days\":1,\"limit\":10}", name, null);
                await served;
                using var result = JsonDocument.Parse(response);
                Assert.Equal(JsonValueKind.Array, result.RootElement.ValueKind);
                Assert.Equal("Encrypted agenda fixture", result.RootElement[0].GetProperty("title").GetString());
                Assert.True(result.RootElement[0].GetProperty("allDay").GetBoolean());
            }
            finally { store.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
