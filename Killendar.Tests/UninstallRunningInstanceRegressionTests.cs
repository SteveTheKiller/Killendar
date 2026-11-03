using System;
using System.Collections.Generic;
using System.IO;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Uninstalling while a copy of Killendar is still running (for example hidden in the
    /// tray) must wait for it and never half-remove the install.</summary>
    public sealed class UninstallRunningInstanceRegressionTests
    {
        private static string NewRoot()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Killendar.exe"), "inert image");
            return root;
        }

        [Fact]
        public void IntegrationWaitsUntilTheRunningInstanceHasExited()
        {
            string root = NewRoot();
            try
            {
                int polls = 0;
                var order = new List<string>();
                int result = UninstallWorkflow.CompleteDeferred(root, new[] { "Killendar.exe" },
                    () => order.Add("registration"), 1, 0, null,
                    () => order.Add("integration-after-" + polls + "-polls"),
                    () => ++polls <= 3, 5000);
                Assert.Equal(0, result);
                Assert.Equal(new[] { "integration-after-4-polls", "registration" }, order);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void InstanceThatNeverExitsReportsBusyAndRemovesNothing()
        {
            string root = NewRoot();
            try
            {
                bool integrationRan = false, metadataRan = false, deleteRan = false;
                string? reported = null;
                int result = UninstallWorkflow.CompleteDeferred(root, new[] { "Killendar.exe" },
                    () => metadataRan = true, 1, 0, error => reported = error.Message,
                    () => integrationRan = true, () => true, 50, _ => deleteRan = true);
                Assert.Equal(1618, result);
                Assert.False(integrationRan);
                Assert.False(metadataRan);
                Assert.False(deleteRan);
                Assert.True(File.Exists(Path.Combine(root, "Killendar.exe")));
                Assert.Equal("Killendar is still running. Close it, then run the uninstaller again.", reported);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void AccessDeniedOnAMappedImageIsRetriedLikeBusy()
        {
            string root = NewRoot();
            try
            {
                int deletes = 0;
                int result = UninstallWorkflow.CompleteDeferred(root, new[] { "Killendar.exe" },
                    () => { }, 3, 0, null, null, null, 0, path =>
                    {
                        if (++deletes < 3) throw new UnauthorizedAccessException("mapped image");
                        File.Delete(path);
                    });
                Assert.Equal(0, result);
                Assert.Equal(3, deletes);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
