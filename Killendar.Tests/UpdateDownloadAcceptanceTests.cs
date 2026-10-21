using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class UpdateDownloadAcceptanceTests
    {
        [Fact]
        public async Task MatchingReleaseChecksumDoesNotAcceptUnsignedExecutable()
        {
            string directory = Path.Combine(TestScratch.Root,
                "download-acceptance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                byte[] unsigned = File.ReadAllBytes(typeof(UpdateDownloadAcceptanceTests).Assembly.Location);
                using var handler = new ReleaseHandler(unsigned);
                using var http = new HttpClient(handler);
                var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                    UpdateService.DownloadAsync("v999.0.0", http, directory));
                Assert.Contains("Authenticode", error.Message);
                Assert.Equal(2, handler.Requests);
                Assert.Empty(Directory.GetFiles(directory));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public async Task WrongReleaseChecksumIsRejectedBeforeWritingCandidate()
        {
            string directory = Path.Combine(TestScratch.Root,
                "download-acceptance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                byte[] unsigned = File.ReadAllBytes(typeof(UpdateDownloadAcceptanceTests).Assembly.Location);
                using var handler = new ReleaseHandler(unsigned, new string('0', 64));
                using var http = new HttpClient(handler);
                var error = await Assert.ThrowsAsync<Exception>(() =>
                    UpdateService.DownloadAsync("v999.0.0", http, directory));
                Assert.Equal("checksum mismatch", error.Message);
                Assert.Equal(2, handler.Requests);
                Assert.Empty(Directory.GetFiles(directory));
            }
            finally { Directory.Delete(directory, true); }
        }

        private sealed class ReleaseHandler : HttpMessageHandler
        {
            private readonly byte[] _executable;
            private readonly string _checksum;
            internal int Requests { get; private set; }

            internal ReleaseHandler(byte[] executable, string? checksum = null)
            {
                _executable = executable;
                using var sha = SHA256.Create();
                _checksum = checksum ?? BitConverter.ToString(sha.ComputeHash(executable)).Replace("-", "");
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests++;
                Assert.Equal("https", request.RequestUri!.Scheme);
                Assert.Equal("github.com", request.RequestUri.Host);
                bool checksum = request.RequestUri.AbsolutePath.EndsWith("/SHA256SUMS.txt",
                    StringComparison.Ordinal);
                Assert.EndsWith(checksum ? "/SHA256SUMS.txt" : "/Killendar.exe",
                    request.RequestUri.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = checksum
                        ? (HttpContent)new StringContent("Killendar.exe " + _checksum + "\n")
                        : new ByteArrayContent(_executable)
                });
            }
        }
    }
}
