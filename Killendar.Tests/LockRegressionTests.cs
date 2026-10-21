using System;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S09: Lock now closes an encrypted calendar and forgets its password, so reading it again
    // takes the password; the command pipe is refused meanwhile. A draft is kept, hidden, rather
    // than asked about, so an idle or Windows lock never waits on a question.
    [Collection("Calendar settings")]
    public sealed class LockRegressionTests
    {
        private const string Request =
            "{\"title\":\"Sneaky\",\"start\":\"2026-10-22T09:00\",\"end\":\"2026-10-22T10:00\"}";

        [Fact]
        public void LockClosesTheCalendarForgetsThePasswordAndRefusesCommands()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.SetPassword("pw");
            var host = new SecurityHostFake();
            var security = new SecurityController(host, fixture.Store);
            security.RememberPassword("pw");

            Assert.True(security.Lock());

            Assert.False(fixture.Store.IsOpen);
            Assert.False(security.RemembersPassword);
            Assert.True(security.IsLocked);
            Assert.True(host.Locked);
            Assert.Contains("error", CalendarCommands.Create(fixture.Store, Request));
        }

        [Fact]
        public void LockKeepsTheDraftWithoutAsking()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.SetPassword("pw");
            fixture.Editor.NewAt(new DateTime(2026, 10, 22, 9, 0, 0));
            fixture.View.FieldTitle = "Half typed";
            var host = new SecurityHostFake();

            new SecurityController(host, fixture.Store).Lock();

            Assert.Equal(0, host.DraftQuestions);
            Assert.Equal("Half typed", fixture.View.FieldTitle);
        }

        [Fact]
        public void UnencryptedCalendarIsNotLocked()
        {
            using var fixture = new AppointmentFixture();
            var host = new SecurityHostFake();
            var security = new SecurityController(host, fixture.Store);

            Assert.False(security.Lock());

            Assert.True(fixture.Store.IsOpen);
            Assert.False(security.IsLocked);
            Assert.Contains(host.Statuses, s => s.StartsWith("Str_Status_LockNeedsPassword"));
        }

        [Theory]
        [InlineData("5", 5)]
        [InlineData("15", 15)]
        [InlineData("30", 30)]
        [InlineData("0", 0)]
        [InlineData("7", 0)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        public void IdleLockIsOffUnlessAKnownChoice(string? setting, int minutes)
            => Assert.Equal(minutes, AutoLock.IdleMinutes(setting));

        [Fact]
        public void IdleLockFiresOnlyAfterTheChosenTime()
        {
            Assert.False(AutoLock.IdleDue(TimeSpan.FromMinutes(4.9), 5));
            Assert.True(AutoLock.IdleDue(TimeSpan.FromMinutes(5), 5));
            Assert.False(AutoLock.IdleDue(TimeSpan.FromHours(3), 0));
        }
    }
}
