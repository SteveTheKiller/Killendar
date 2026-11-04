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
