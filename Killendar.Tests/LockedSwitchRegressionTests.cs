using System;
using System.IO;
using Killendar.Features;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // The Killendars button works while locked: another Killendar can be opened without the
    // locked one's password, and anything short of a successful open leaves the lock in place.
    [Collection("Calendar settings")]
    public sealed class LockedSwitchRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, "locked-switch", Guid.NewGuid().ToString("N"));
        private readonly string? _savedDir = Settings.Get("KillendarDataDir");
        private readonly string? _savedActive = Settings.Get("ActiveKillendar");
        private readonly EventStore _store = new EventStore();
        private readonly SecurityHostFake _host = new SecurityHostFake();
        private readonly SecurityController _security;

        public LockedSwitchRegressionTests()
        {
            Directory.CreateDirectory(_root);
            EventStore.SetDataDir(_root);
            var other = new EventStore();
            other.Open(Path.Combine(_root, "Other.kcal"));
            other.Close();
            _store.Open(Path.Combine(_root, "Locked.kcal"));
            _store.SetPassword("pw");
            EventStore.SetActive("Locked.kcal");
            _security = new SecurityController(_host, _store);
            _security.RememberPassword("pw");
            Assert.True(_security.Lock());
        }

        public void Dispose()
        {
            _store.Close();
            Settings.Set("KillendarDataDir", _savedDir ?? "");
            Settings.Set("ActiveKillendar", _savedActive ?? "");
            Directory.Delete(_root, true);
        }

        [Fact]
        public void PickingAnotherKillendarOpensItAndEndsTheLock()
        {
            Assert.True(_security.ApplyLockedSelection("Other.kcal", "Locked.kcal"));

            Assert.False(_security.IsLocked);
            Assert.False(_host.Locked);
            Assert.True(_store.IsOpen);
            Assert.Equal("Other.kcal", EventStore.ActiveFile, ignoreCase: true);
        }

        [Fact]
        public void ClosingTheDialogWithoutAPickStaysLocked()
        {
            Assert.False(_security.ApplyLockedSelection(null, "Locked.kcal"));

            Assert.True(_security.IsLocked);
            Assert.False(_store.IsOpen);
            Assert.Equal("Locked.kcal", EventStore.ActiveFile, ignoreCase: true);
        }

        [Fact]
        public void KeepingTheHiddenDraftStaysLocked()
        {
            _host.DiscardDraft = false;

            Assert.False(_security.ApplyLockedSelection("Other.kcal", "Locked.kcal"));

            Assert.True(_security.IsLocked);
            Assert.False(_store.IsOpen);
            Assert.Equal("Locked.kcal", EventStore.ActiveFile, ignoreCase: true);
        }

        [Fact]
        public void AKillendarThatWillNotOpenLeavesTheLockOnTheOldOne()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Broken.kcal"));

            Assert.False(_security.ApplyLockedSelection("Broken.kcal", "Locked.kcal"));

            Assert.True(_security.IsLocked);
            Assert.False(_store.IsOpen);
            Assert.Equal("Locked.kcal", EventStore.ActiveFile, ignoreCase: true);
        }
    }
}
