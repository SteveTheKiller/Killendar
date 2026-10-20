using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class NativeCacheRegressionTests
    {
        [Fact]
        public void PublicationRepairsSameLengthCorruption()
        {
            string directory = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\native-cache",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "fixture.bin");
            try
            {
                File.WriteAllBytes(target, [9, 8, 7, 6]);

                SqlCipherBootstrap.PublishVerifiedCache(target, [1, 2, 3, 4]);

                Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(target));
                Assert.Single(Directory.GetFiles(directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PublicationCreatesMissingOrRepairsTruncatedCache(bool truncated)
        {
            WithTarget(target =>
            {
                if (truncated) File.WriteAllBytes(target, [1]);
                SqlCipherBootstrap.PublishVerifiedCache(target, [1, 2, 3, 4]);
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(target));
            });
        }

        [Fact]
        public void MatchingCacheIsReusedWithoutReplacingLockedFile()
        {
            WithTarget(target =>
            {
                File.WriteAllBytes(target, [1, 2, 3, 4]);
                using var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
                SqlCipherBootstrap.PublishVerifiedCache(target, [1, 2, 3, 4]);
                Assert.Equal(1, locked.ReadByte());
            });
        }

        [Fact]
        public void DeniedPublicationReportsFailureAndPreservesExistingBytes()
        {
            WithTarget(target =>
            {
                File.WriteAllBytes(target, [9, 8, 7, 6]);
                using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Exception? error = Record.Exception(() =>
                        SqlCipherBootstrap.PublishVerifiedCache(target, [1, 2, 3, 4]));
                    Assert.True(error is IOException || error is UnauthorizedAccessException);
                }
                Assert.Equal(new byte[] { 9, 8, 7, 6 }, File.ReadAllBytes(target));
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ConcurrentPublicationLeavesOneCompleteVerifiedCache(bool existing)
        {
            WithTarget(target =>
            {
                if (existing) File.WriteAllBytes(target, [9, 8, 7, 6]);
                Task.WaitAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
                    SqlCipherBootstrap.PublishVerifiedCache(target, [1, 2, 3, 4]))).ToArray());
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(target));
            });
        }

        private static void WithTarget(Action<string> action)
        {
            string directory = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\native-cache",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                action(Path.Combine(directory, "fixture.bin"));
                Assert.Single(Directory.GetFiles(directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
