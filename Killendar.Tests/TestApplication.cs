using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Killendar.Tests
{
    /// <summary>One WPF Application per test AppDomain, owned by a long-lived STA thread.
    /// Test classes run in parallel, so a bare "if (Application.Current == null) new Application()"
    /// raced (the second constructor throws), and an Application created on a short-lived test
    /// thread was left on a dead dispatcher, so a later test that touched its resources failed.</summary>
    internal static class TestApplication
    {
        private static readonly Lazy<Dispatcher> Owner = new Lazy<Dispatcher>(Start, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>The dispatcher that owns Application.Current.</summary>
        public static Dispatcher Dispatcher => Owner.Value;

        public static void Ensure() => _ = Owner.Value;

        private static Dispatcher Start()
        {
            Dispatcher? dispatcher = null;
            Exception? failure = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    dispatcher = Dispatcher.CurrentDispatcher;
                    if (Application.Current == null) new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }
                catch (Exception ex) { failure = ex; }
                ready.Set();
                if (failure == null) Dispatcher.Run();
            }) { IsBackground = true, Name = "Test Application" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("The test Application thread did not start.");
            if (failure != null) throw new InvalidOperationException("The test Application could not be created.", failure);
            return dispatcher!;
        }
    }
}
