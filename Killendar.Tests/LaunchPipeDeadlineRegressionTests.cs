using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class LaunchPipeDeadlineRegressionTests
    {
        [Fact]
        public void StalledClientCannotBlockLaterValidLaunch()
        {
            string id = "Killendar.Tests.Launch." + Guid.NewGuid().ToString("N");
            using var received = new ManualResetEventSlim();
            string? delivered = null;
            using var primary = new SingleInstanceGuard(id, action => action(), path =>
            {
                delivered = path;
                received.Set();
            }, 150);
            Assert.True(primary.Claim(null));
            using var stalled = new NamedPipeClientStream(".", id + "-" +
                Process.GetCurrentProcess().SessionId, PipeDirection.Out);
            stalled.Connect(2000);
            Thread.Sleep(300);
            using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 150);

            Assert.False(secondary.Claim("requested.kcal"));
            Assert.True(received.Wait(1000), "A stalled launch client blocked a later valid launch.");
            Assert.Equal("requested.kcal", delivered);
            Assert.True(secondary.ForwardingSucceeded);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RejectedClientDoesNotDispatchAndLaterLaunchIsAccepted(bool oversized)
        {
            string id = "Killendar.Tests.Launch." + Guid.NewGuid().ToString("N");
            using var received = new ManualResetEventSlim();
            string? delivered = null;
            int count = 0;
            using var primary = new SingleInstanceGuard(id, action => action(), path =>
            {
                delivered = path;
                Interlocked.Increment(ref count);
                received.Set();
            }, 500);
            Assert.True(primary.Claim(null));
            using (var rejected = new NamedPipeClientStream(".", id + "-" +
                Process.GetCurrentProcess().SessionId, PipeDirection.Out))
            {
                rejected.Connect(2000);
                byte[] bytes = Encoding.UTF8.GetBytes(oversized
                    ? new string('x', 131073) + "\n" : "unterminated.kcal");
                try { rejected.Write(bytes, 0, bytes.Length); }
                catch (IOException) when (oversized) { }
            }
            Thread.Sleep(100);
            Assert.False(received.IsSet);
            using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 1000);
            Assert.False(secondary.Claim("valid.ics"));
            Assert.True(secondary.ForwardingSucceeded);
            Assert.True(received.Wait(2000));
            Assert.Equal("valid.ics", delivered);
            Assert.Equal(1, Volatile.Read(ref count));
        }

        [Theory]
        [InlineData("embedded\nnewline")]
        [InlineData("embedded\rcarriage")]
        public void InvalidForwardedPayloadReportsFailure(string payload)
        {
            string id = "Killendar.Tests.Launch." + Guid.NewGuid().ToString("N");
            using var primary = new SingleInstanceGuard(id, action => action(), _ => { }, 150);
            Assert.True(primary.Claim(null));
            using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 150);
            Assert.False(secondary.Claim(payload));
            Assert.False(secondary.ForwardingSucceeded);
        }

        [Fact]
        public void UnreachablePrimaryReportsForwardingFailureWithinDeadline()
        {
            string id = "Killendar.Tests.Launch." + Guid.NewGuid().ToString("N");
            using var mutex = new Mutex(true, @"Local\" + id + "-SingleInstance");
            try
            {
                using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 150);
                var elapsed = Stopwatch.StartNew();
                Assert.False(secondary.Claim("valid.ics"));
                Assert.False(secondary.ForwardingSucceeded);
                Assert.True(elapsed.ElapsedMilliseconds < 2000);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
