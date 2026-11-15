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
    public sealed class PressActivationAutomationTests
    {
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
