using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The order an uninstall removes things in, so a failure always leaves a working
    /// uninstall entry, and what a machine-wide uninstall leaves for the next restart.</summary>
    public sealed class UninstallOrderRegressionTests
    {
        [Fact]
        public void IntegrationFailureKeepsTheInstalledImageAndRegistration()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string image = Path.Combine(root, "Killendar.exe");
                File.WriteAllText(image, "inert image");
                bool registrationRemoved = false;
                int result = UninstallWorkflow.CompleteDeferred(root, new[] { "Killendar.exe" },
                    () => registrationRemoved = true, 1, 0, null,
                    () => throw new IOException("An installed shortcut remains."));
                Assert.NotEqual(0, result);
                // The uninstall entry still points at a real program, so the user can run it again.
                Assert.True(File.Exists(image));
                Assert.False(registrationRemoved);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void IntegrationGoesFirstFilesNextRegistrationLast()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string image = Path.Combine(root, "Killendar.exe");
                File.WriteAllText(image, "inert image");
                var order = new List<string>();
                Assert.Equal(0, UninstallWorkflow.CompleteDeferred(root, new[] { "Killendar.exe" },
                    () => order.Add(File.Exists(image) ? "registration-before-files" : "registration"), 1, 0, null,
                    () => order.Add(File.Exists(image) ? "integration" : "integration-after-files")));
                Assert.Equal(new[] { "integration", "registration" }, order);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void ElevatedUninstallIsWaitedForWhileItsConfirmationIsOpen()
        {
            var wait = typeof(UninstallWorkflow).GetMethod(nameof(UninstallWorkflow.RequestElevation))!
                .GetParameters().Single(parameter => parameter.Name == "waitMilliseconds");
            Assert.Equal(Timeout.Infinite, (int)wait.DefaultValue!);
        }

        [Fact]
        public void MachineCleanupSchedulesTheWorkerItsFolderAndTheEmptyInstallFolder()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", Guid.NewGuid().ToString("N"));
            string install = Path.Combine(root, "Killendar");
            string operation = Path.Combine(install, "Uninstall-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(operation);
            try
            {
                foreach (string name in new[] { DeferredUninstall.WorkerName, DeferredUninstall.WorkerName + ".config", DeferredUninstall.ReceiptName })
                    File.WriteAllText(Path.Combine(operation, name), "x");
                var scheduled = new List<string>();
                DeferredUninstall.ScheduleMachineCleanup(operation, install, path => { scheduled.Add(path); return true; });
                Assert.Equal(new[]
                {
                    Path.Combine(operation, DeferredUninstall.WorkerName),
                    Path.Combine(operation, DeferredUninstall.WorkerName + ".config"),
                    Path.Combine(operation, DeferredUninstall.ReceiptName),
                    operation,
                    install,
                }, scheduled);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
