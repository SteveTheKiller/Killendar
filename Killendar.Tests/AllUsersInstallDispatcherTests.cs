using System;
using System.Threading;
using System.Windows.Threading;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A dispatcher thread with no window, standing in for the interface thread.</summary>
    public sealed class AllUsersInstallDispatcherTests
    {
        [Fact]
        public void TheInterfaceThreadKeepsPumpingWhileTheElevatedChildRuns()
        {
            Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(ready.Wait(5000));

            using var childRunning = new ManualResetEventSlim();
            using var childExits = new ManualResetEventSlim();
            using var finished = new ManualResetEventSlim();
            int childThread = 0, removalThread = 0, relaunchThread = 0;
            bool installed = false;
            try
            {
                dispatcher!.BeginInvoke(new Action(async () =>
                {
                    installed = await AllUsersInstall.RunAsync(false, @"C:\Users\pat\Desktop\Killendar.lnk",
                        @"C:\Program Files\Killendar\Killendar.exe",
                        () =>
                        {
                            childThread = Environment.CurrentManagedThreadId;
                            childRunning.Set();
                            return childExits.Wait(5000);
                        },
                        () => removalThread = Environment.CurrentManagedThreadId,
                        (_, _) => { },
                        () => { relaunchThread = Environment.CurrentManagedThreadId; return true; });
                    finished.Set();
                }));

                Assert.True(childRunning.Wait(5000));
                // The child's WM_SETTINGCHANGE broadcast needs this thread to answer messages.
                using var pumped = new ManualResetEventSlim();
                dispatcher.BeginInvoke(new Action(pumped.Set));
                Assert.True(pumped.Wait(2000), "The interface thread stopped pumping while the elevated child ran.");

                childExits.Set();
                Assert.True(finished.Wait(5000));
                Assert.True(installed);
                Assert.NotEqual(thread.ManagedThreadId, childThread);
                Assert.Equal(thread.ManagedThreadId, removalThread);
                Assert.Equal(thread.ManagedThreadId, relaunchThread);
            }
            finally
            {
                childExits.Set();
                dispatcher!.InvokeShutdown();
                thread.Join(5000);
            }
        }
    }
}
