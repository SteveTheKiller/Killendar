using System;
using System.IO;
using Killendar.Features;
using Killendar.Services;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class SmallFixesRegressionTests
    {
        // GE-001: the hover band floors to its slot, so a click anywhere inside the band must
        // create in that band, not at the next slot down.
        [Theory]
        [InlineData(0.0, 0)]
        [InlineData(20.0, 0)]   // lower half of the 9:00 to 9:30 band at 60 px an hour
        [InlineData(29.9, 0)]
        [InlineData(30.0, 1)]
        [InlineData(10000.0, 47)]
        public void AClickCreatesInTheBandUnderThePointer(double y, int slot)
            => Assert.Equal(slot, TimeGridViewSlot(y));

        private static int TimeGridViewSlot(double y) => TimeGridView.SlotIndexAt(y, 60, 30);

        // GD-009: the last fallback read a whole date as midnight.
        [Theory]
        [InlineData("2026-11-14")]
        [InlineData("11/14/2026 9:00")]
        public void ATimeFieldRejectsADate(string raw)
            => Assert.False(AppointmentEditor.TryParseTime(raw, out _));

        [Fact]
        public void ATimeFieldStillReadsAPlainTime()
        {
            Assert.True(AppointmentEditor.TryParseTime("9:30 PM", out var t));
            Assert.Equal(new TimeSpan(21, 30, 0), t);
        }

        // GE-007: monthly paging kept the clamped day, so January 31 went to February 28 and
        // then March 28.
        [Fact]
        public void MonthlyPagingKeepsTheDayItStartedOn()
        {
            Exception? failure = null;
            var thread = new System.Threading.Thread(() =>
            {
                try { PageThroughMonths(); }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The paging test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private static void PageThroughMonths()
        {
            string? saved = Settings.Get(AgendaView.HorizonSetting);
            try
            {
                Settings.Set(AgendaView.HorizonSetting, "60");
                var view = new AgendaView();
                var day = view.Step(new DateTime(2027, 1, 31), 1);
                Assert.Equal(new DateTime(2027, 2, 28), day);
                day = view.Step(day, 1);
                Assert.Equal(new DateTime(2027, 3, 31), day);
                day = view.Step(day, -1);
                Assert.Equal(new DateTime(2027, 2, 28), day);
                // A day chosen some other way starts a new run from that day.
                Assert.Equal(new DateTime(2027, 4, 15), view.Step(new DateTime(2027, 3, 15), 1));
            }
            finally { Settings.Set(AgendaView.HorizonSetting, saved ?? ""); }
        }

        // GE-010: the crash hook says it runs first, so it must come before every startup step.
        [Fact]
        public void TheCrashHookIsTheFirstStartupStep()
        {
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Killendar.csproj"))) root = Path.GetDirectoryName(root);
            Assert.NotNull(root);
            string source = File.ReadAllText(Path.Combine(root!, "App.xaml.cs"));
            int startup = source.IndexOf("protected override void OnStartup(", StringComparison.Ordinal);
            int body = source.IndexOf('{', startup) + 1;
            int firstStatement = source.IndexOf(';', body);
            Assert.Contains("HookCrashLogging()", source.Substring(body, firstStatement - body));
        }
    }

    // GE-003: a reload whose open fails left the views drawing the store it had just closed.
    [Collection("Calendar settings")]
    public sealed class ReloadFailureRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, "reload-fail", Guid.NewGuid().ToString("N"));
        private readonly string? _savedDir = Settings.Get("KillendarDataDir");
        private readonly string? _savedActive = Settings.Get("ActiveKillendar");
        private readonly EventStore _store = new EventStore();
        private readonly SecurityHostFake _host = new SecurityHostFake();

        public ReloadFailureRegressionTests()
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
        public void AFailedReloadStillRefreshesTheViews()
        {
            // A folder where the calendar file is expected cannot be opened and is not read as
            // an encrypted file, so no prompt appears.
            Directory.CreateDirectory(Path.Combine(_root, "Broken.kcal"));
            EventStore.SetActive("Broken.kcal");

            new SecurityController(_host, _store).ReloadActive();

            Assert.False(_store.IsOpen);
            Assert.True(_host.Refreshes > 0, "The views were not refreshed after the store closed.");
        }
    }
}
