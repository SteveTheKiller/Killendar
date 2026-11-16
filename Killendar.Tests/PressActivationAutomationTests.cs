using System;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    // KD150-R3: a reminder row opens on Enter, Space and a click, so a screen reader must hear it
    // as a button and be able to invoke it, not find a nameless pane.
    [Collection("Calendar settings")]
    public sealed class PressActivationAutomationTests
    {
        [Fact]
        public void YearDayInvocationOpensItsDate()
        {
            RunSta(() =>
            {
                using var fixture = new AppointmentFixture();
                var view = new Killendar.Views.YearView { Anchor = new DateTime(2026, 11, 15) };
                view.Initialize(fixture.Store);
                DateTime? opened = null;
                view.DayOpened += day => opened = day;
                var cell = FindYearDay(view);
                Assert.NotNull(cell);
                var peer = UIElementAutomationPeer.CreatePeerForElement(cell!);
                var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));

                invoke.Invoke();

                Assert.Equal(new DateTime(2026, 1, 1), opened);
            });
        }

        private static Killendar.Views.YearView.DayCellGrid? FindYearDay(System.Windows.DependencyObject root)
        {
            if (root is Killendar.Views.YearView.DayCellGrid day) return day;
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
                if (child is System.Windows.DependencyObject node && FindYearDay(node) is { } found) return found;
            return null;
        }

        [Fact]
        public void PressableRowIsAnInvokableButtonForScreenReaders()
        {
            RunSta(() =>
            {
                int opened = 0;
                var row = new PressablePanel();
                AutomationProperties.SetName(row, "Standup, 9:00");
                PressActivation.Attach(row, () => opened++);

                var peer = UIElementAutomationPeer.CreatePeerForElement(row);

                Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
                Assert.Equal("Standup, 9:00", peer.GetName());
                var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
                invoke.Invoke();
                Assert.Equal(1, opened);
            });
        }

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    TestApplication.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The press activation test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
