using System;
using System.Threading;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class SingleInstanceTeardownTests
    {
        [Fact]
        public void ALaunchIntoAShuttingDownDispatcherReportsFailure()
        {
            string id = "Killendar.Tests.Teardown." + Guid.NewGuid().ToString("N");
            int delivered = 0;
            // A dispatcher that has begun shutting down drops what it is handed.
            using var primary = new SingleInstanceGuard(id, _ => { }, _ => Interlocked.Increment(ref delivered), 300);
            Assert.True(primary.Claim(null));

            using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 300);
            Assert.False(secondary.Claim("teardown.ics"));

            Assert.False(secondary.ForwardingSucceeded, "A launch nobody handled must not report success.");
            Assert.Equal(0, Volatile.Read(ref delivered));
        }

        [Fact]
        public void ALaunchTheRunningInstanceAcceptsStillReportsSuccess()
        {
            string id = "Killendar.Tests.Teardown." + Guid.NewGuid().ToString("N");
            string? delivered = null;
            using var received = new ManualResetEventSlim();
            using var primary = new SingleInstanceGuard(id, action => action(), path =>
            {
                delivered = path;
                received.Set();
            }, 2000);
            Assert.True(primary.Claim(null));

            using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 2000);
            Assert.False(secondary.Claim("accepted.ics"));

            Assert.True(secondary.ForwardingSucceeded);
            Assert.True(received.Wait(5000));
            Assert.Equal("accepted.ics", delivered);
        }

        [Fact]
        public void AnOlderClientThatHangsUpBeforeTheAnswerLeavesTheListenerRunning()
        {
            // A 1.4.0 copy opens the pipe write-only, writes its path and disconnects. The reply
            // byte then meets a closed pipe, and the next launch must still be forwarded.
            string id = "Killendar.Tests.Teardown." + Guid.NewGuid().ToString("N");
            var received = new System.Collections.Concurrent.ConcurrentQueue<string>();
            using var primary = new SingleInstanceGuard(id, action => action(), received.Enqueue, 2000);
            Assert.True(primary.Claim(null));
            Thread.Sleep(200);

            using (var old = new System.IO.Pipes.NamedPipeClientStream(".",
                id + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId, System.IO.Pipes.PipeDirection.Out))
            {
                old.Connect(2000);
                byte[] line = System.Text.Encoding.UTF8.GetBytes("older.ics\n");
                old.Write(line, 0, line.Length);
                old.Flush();
            }

            using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 2000);
            Assert.False(secondary.Claim("newer.ics"));
            Assert.True(secondary.ForwardingSucceeded);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (received.Count < 2 && DateTime.UtcNow < deadline) Thread.Sleep(20);
            Assert.Equal(new[] { "older.ics", "newer.ics" }, received.ToArray());
        }

        [Fact]
        public void AnOlderInstanceWhosePipeOnlyReadsStillReceivesTheLaunch()
        {
            string id = "Killendar.Tests.Teardown." + Guid.NewGuid().ToString("N");
            using var mutex = new Mutex(true, @"Local\" + id + "-SingleInstance");
            string? received = null;
            var server = new Thread(() =>
            {
                // An earlier build's server: inbound only, reads one line, never answers.
                using var pipe = PipeGuard.CreateServer(id + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId,
                    System.IO.Pipes.PipeDirection.In);
                pipe.WaitForConnection();
                using var reader = new System.IO.StreamReader(pipe);
                received = reader.ReadLine();
            }) { IsBackground = true };
            server.Start();
            try
            {
                Thread.Sleep(200);
                using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 2000);
                Assert.False(secondary.Claim("older.ics"));
                Assert.True(server.Join(5000));
                Assert.Equal("older.ics", received);
                Assert.True(secondary.ForwardingSucceeded);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        [Fact]
        public void ADisposedGuardLetsTheNextLaunchBecomeTheInstance()
        {
            string id = "Killendar.Tests.Teardown." + Guid.NewGuid().ToString("N");
            var primary = new SingleInstanceGuard(id, action => action(), _ => { }, 300);
            Assert.True(primary.Claim(null));
            primary.Dispose();

            using var next = new SingleInstanceGuard(id, action => action(), _ => { }, 300);
            Assert.True(next.Claim("after.ics"));
        }
    }
}
