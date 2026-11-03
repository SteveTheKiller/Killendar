using System;
using System.IO;
using Killendar.Features;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Manage Killendars closes the open store for its whole lifetime. An idle or Windows lock that
    // comes while it is open must still lock an encrypted Killendar: the remembered password is
    // forgotten, and closing the dialog asks for it instead of quietly reopening the calendar.
    [Collection("Calendar settings")]
    public sealed class ManageLockRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, "manage-lock", Guid.NewGuid().ToString("N"));
        private readonly string? _savedDir = Settings.Get("KillendarDataDir");
        private readonly string? _savedActive = Settings.Get("ActiveKillendar");
        private readonly EventStore _store = new EventStore();
        private readonly SecurityHostFake _host = new SecurityHostFake();
        private readonly SecurityController _security;

        public ManageLockRegressionTests()
        {
            Directory.CreateDirectory(_root);
            EventStore.SetDataDir(_root);
            _store.Open(Path.Combine(_root, "Secret.kcal"));
            _store.SetPassword("pw");
            EventStore.SetActive("Secret.kcal");
            _security = new SecurityController(_host, _store);
            _security.RememberPassword("pw");
        }

        public void Dispose()
        {
            _store.Close();
            Settings.Set("KillendarDataDir", _savedDir ?? "");
            Settings.Set("ActiveKillendar", _savedActive ?? "");
            Directory.Delete(_root, true);
        }

        [Fact]
        public void ALockWhileManageIsOpenForgetsThePasswordAndStaysLocked()
        {
            bool lockedInside = false;
            _security.ShowKillendars(() =>
            {
                Assert.True(_security.ManagingEncrypted);
                lockedInside = _security.Lock();
                return null;
            });

            Assert.True(lockedInside);
            Assert.True(_security.IsLocked);
            Assert.False(_security.RemembersPassword);
            Assert.False(_store.IsOpen);
            Assert.True(_host.Locked);
            Assert.False(_security.ManagingEncrypted);
        }

        [Fact]
        public void ManageWithoutALockReopensWithTheSessionPassword()
        {
            _security.ShowKillendars(() => null);

            Assert.False(_security.IsLocked);
            Assert.True(_store.IsOpen);
            Assert.False(_security.ManagingEncrypted);
        }
    }
}
