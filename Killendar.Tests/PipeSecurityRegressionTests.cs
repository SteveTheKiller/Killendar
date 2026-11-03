using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class PipeSecurityRegressionTests
    {
        [Fact]
        public void CalendarServerGrantsOnlyTheCurrentUser()
        {
            using var server = CalendarCommandPipe.CreateServer("Killendar-test-" + Guid.NewGuid().ToString("N"));
            AssertOnlyCurrentUserAllowed(server);
        }

        [Fact]
        public void SendRefusesAServerOwnedBySomeoneElse()
        {
            string name = "Killendar-test-" + Guid.NewGuid().ToString("N");
            using var server = CalendarCommandPipe.CreateServer(name);
            var wait = server.WaitForConnectionAsync();
            string? received = null;
            var serve = System.Threading.Tasks.Task.Run(async () =>
            {
                await wait;
                CalendarCommandPipe.Serve(server, request => { received = request; return "{\"ok\":true}"; });
            });

            var failure = Assert.Throws<InvalidOperationException>(() => CalendarCommandPipe.Send(
                "secret appointment", name, _ => new SecurityIdentifier("S-1-5-21-1-2-3-1001")));

            Assert.IsType<System.IO.IOException>(failure.InnerException);
            Assert.Null(received);
        }

        [Fact]
        public void ServerLoopBacksOffWhenThePipeCannotBeCreated()
        {
            int attempts = 0;
            var delays = new System.Collections.Generic.List<double>();

            PipeGuard.RunServerLoop(
                () => { attempts++; throw new UnauthorizedAccessException("name is taken"); },
                _ => { },
                () => attempts == 8,
                delay => delays.Add(delay.TotalMilliseconds));

            Assert.Equal(new double[] { 250, 500, 1000, 2000, 4000, 5000, 5000, 5000 }, delays);
        }

        [Fact]
        public void LaunchGuardSurvivesAPipeNameItCannotCreate()
        {
            string id = "KillendarTest" + Guid.NewGuid().ToString("N");
            int attempts = 0;
            var delays = new System.Collections.Generic.List<double>();
            string? forwarded = null;
            using var forwardedSignal = new System.Threading.ManualResetEventSlim();
            using var primary = new SingleInstanceGuard(id, action => action(), path =>
            {
                forwarded = path;
                forwardedSignal.Set();
            });
            primary.Delay = delay => { lock (delays) delays.Add(delay.TotalMilliseconds); };
            var real = primary.ServerFactory;
            primary.ServerFactory = name =>
            {
                if (System.Threading.Interlocked.Increment(ref attempts) <= 2)
                    throw new UnauthorizedAccessException("name is taken");
                return real(name);
            };
            Assert.True(primary.Claim(null));

            using var secondary = new SingleInstanceGuard(id, action => action(), _ => { }, 10000);
            Assert.False(secondary.Claim(@"C:\calendars\work.killendar"));

            Assert.True(forwardedSignal.Wait(10000));
            Assert.Equal(@"C:\calendars\work.killendar", forwarded);
            lock (delays) Assert.Equal(new double[] { 250, 500 }, delays);
        }

        [Fact]
        public void LaunchForwardRefusesAServerOwnedBySomeoneElse()
        {
            string id = "KillendarTest" + Guid.NewGuid().ToString("N");
            string? forwarded = null;
            using var primary = new SingleInstanceGuard(id, action => action(), path => forwarded = path);
            Assert.True(primary.Claim(null));

            using var secondary = new SingleInstanceGuard(id, action => action(), _ => { })
            {
                ServerOwner = _ => new SecurityIdentifier("S-1-5-21-1-2-3-1001")
            };
            Assert.False(secondary.Claim(@"C:\calendars\work.killendar"));

            Assert.False(secondary.ForwardingSucceeded);
            Assert.Null(forwarded);
        }

        private static void AssertOnlyCurrentUserAllowed(NamedPipeServerStream server)
        {
            var rules = server.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
            Assert.NotEmpty(rules);
            foreach (AuthorizationRule rule in rules)
            {
                Assert.Equal(WindowsIdentity.GetCurrent().User, rule.IdentityReference);
                Assert.Equal(AccessControlType.Allow, ((PipeAccessRule)rule).AccessControlType);
            }
        }
    }
}
