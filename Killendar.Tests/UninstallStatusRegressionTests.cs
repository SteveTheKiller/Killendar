using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class UninstallStatusRegressionTests
    {
        [Fact]
        public void CanceledElevationReturnsCancellationInsteadOfSuccess()
        {
            int? result = UninstallWorkflow.RequestElevation(true, () => false,
                () => throw new Win32Exception(1223), error => throw error);

            Assert.Equal(1223, result);
        }

        [Fact]
        public void MissingElevatedProcessReturnsFailure()
        {
            int? result = UninstallWorkflow.RequestElevation(true, () => false, () => null, _ => { });
            Assert.Equal(1, result);
        }

        [Fact]
        public void ElevatedWaitIsBoundedAndReportsBusy()
        {
            var elapsed = Stopwatch.StartNew();
            int? result = UninstallWorkflow.RequestElevation(true, () => false,
                Process.GetCurrentProcess, _ => { }, 20);
            Assert.Equal(1618, result);
            Assert.True(elapsed.ElapsedMilliseconds < 5000);
        }

        [Fact]
        public void PerUserUninstallDoesNotRequestElevation()
        {
            Assert.Null(UninstallWorkflow.RequestElevation(false, () => throw new System.Exception(),
                () => throw new System.Exception(), _ => { }));
        }

        [Fact]
        public void LockedOwnedFileKeepsTheUninstallMarkerAndReportsFailure()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall",
                System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string image = Path.Combine(root, "Killendar.exe");
            string marker = Path.Combine(root, "uninstall-marker.txt");
            File.WriteAllText(image, "inert owned image");
            File.WriteAllText(marker, "registered");
            try
            {
                using var locked = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.None);
                int result = UninstallWorkflow.CompleteDeferred(root, new[] { "Killendar.exe" },
                    () => File.Delete(marker), 1, 0);
                Assert.Equal(1618, result);
                Assert.True(File.Exists(image));
                Assert.True(File.Exists(marker));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void CompletionRemovesOnlyOwnedFilesBeforeFinalizingMetadata()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string image = Path.Combine(root, "Killendar.exe");
                string calendar = Path.Combine(root, "Work.kcal");
                File.WriteAllText(image, "inert image");
                File.WriteAllText(calendar, "preserved calendar");
                bool finalized = false;
                Assert.Equal(0, UninstallWorkflow.CompleteDeferred(root, new[] { "Killendar.exe" }, () =>
                {
                    Assert.False(File.Exists(image));
                    Assert.Equal("preserved calendar", File.ReadAllText(calendar));
                    finalized = true;
                }, 1, 0));
                Assert.True(finalized);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void MetadataFailureDoesNotReportCompletion()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Assert.Equal(5, UninstallWorkflow.CompleteDeferred(root, new[] { "Killendar.exe" },
                    () => throw new System.UnauthorizedAccessException(), 1, 0));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void DeferredLaunchReturnsPendingWithAnObservableReceipt()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "status.json");
                bool launched = false;
                int result = UninstallWorkflow.StartDeferred(path, () => { launched = true; return true; });
                Assert.Equal(997, result);
                Assert.True(launched);
                var receipt = UninstallWorkflow.ReadReceipt(path);
                Assert.Equal("Pending", receipt.State);
                Assert.Equal(997, receipt.ExitCode);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void FailedDeferredLaunchPublishesAFailedReceipt()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "status.json");
                Assert.Equal(1, UninstallWorkflow.StartDeferred(path, () => false));
                var receipt = UninstallWorkflow.ReadReceipt(path);
                Assert.Equal("Failed", receipt.State);
                Assert.Equal(1, receipt.ExitCode);
                Assert.NotEmpty(receipt.Error);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void AlreadyMissingOwnedDirectoryCanFinalizeMetadataAfterVerifiedAbsence()
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string missing = Path.Combine(root, "already-removed");
                bool finalized = false;
                int result = UninstallWorkflow.CompleteDeferred(missing, new[] { "Killendar.exe" }, () =>
                {
                    Assert.False(Directory.Exists(missing));
                    finalized = true;
                }, 1, 0);
                Assert.Equal(0, result);
                Assert.True(finalized);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
