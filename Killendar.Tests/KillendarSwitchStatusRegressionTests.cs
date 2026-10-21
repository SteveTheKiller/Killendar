using System;
using System.IO;
using System.Linq;
using Killendar.Features;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-014: switching Killendars only reports "Opened" after something actually opened.
    [Collection("Calendar settings")]
    public sealed class KillendarSwitchStatusRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, "switch-status", Guid.NewGuid().ToString("N"));
        private readonly string? _savedDir = Settings.Get("KillendarDataDir");
        private readonly string? _savedActive = Settings.Get("ActiveKillendar");
        private readonly EventStore _store = new EventStore();
        private readonly SecurityHostFake _host = new SecurityHostFake();

        public KillendarSwitchStatusRegressionTests()
        {
            Directory.CreateDirectory(_root);
            EventStore.SetDataDir(_root);
        }

        public void Dispose()
        {
            _store.Close();
            Settings.Set("KillendarDataDir", _savedDir ?? "");
            Settings.Set("ActiveKillendar", _savedActive ?? "");
            Directory.Delete(_root, true);
        }

        [Fact]
        public void FailedSelectionAndFailedFallbackDoNotReportOpened()
        {
            // A folder where a calendar file is expected cannot be opened by SQLite and is not
            // mistaken for an encrypted file, so no unlock prompt can appear.
            Directory.CreateDirectory(Path.Combine(_root, "Selected.kcal"));
            Directory.CreateDirectory(Path.Combine(_root, "Previous.kcal"));
            EventStore.SetActive("Previous.kcal");

            Security().ApplyKillendarSelection("Selected.kcal", "Previous.kcal", _root, Path.Combine(_root, "Previous.kcal"));

            Assert.False(_store.IsOpen);
            Assert.DoesNotContain(_host.Statuses, s => s.StartsWith("Str_Status_Opened", StringComparison.Ordinal));
            Assert.StartsWith("Str_Status_LoadError", _host.Statuses.Last());
        }

        [Fact]
        public void FailedSelectionWithWorkingFallbackKeepsTheFailureVisible()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Selected.kcal"));
            string previous = Path.Combine(_root, "Previous.kcal");
            _store.Open(previous);
            _store.Close();
            EventStore.SetActive("Previous.kcal");

            Security().ApplyKillendarSelection("Selected.kcal", "Previous.kcal", _root, previous);

            Assert.True(_store.IsOpen);
            Assert.Equal(previous, _store.FilePath, ignoreCase: true);
            Assert.StartsWith("Str_Status_LoadError", _host.Statuses.Last());
        }

        [Fact]
        public void SuccessfulSelectionReportsOpened()
        {
            string selected = Path.Combine(_root, "Selected.kcal");
            _store.Open(selected);
            _store.Close();
            EventStore.SetActive("Previous.kcal");

            Security().ApplyKillendarSelection("Selected.kcal", "Previous.kcal", _root, Path.Combine(_root, "Previous.kcal"));

            Assert.True(_store.IsOpen);
            Assert.StartsWith("Str_Status_Opened", _host.Statuses.Last());
        }

        private SecurityController Security() => new SecurityController(_host, _store);
    }
}
