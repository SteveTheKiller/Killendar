using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // HttpDownload.CopyToAsync against a fake handler, so no network is involved: the size cap
    // (declared and streamed) and the whole-transfer deadline (headers and body).
    public sealed class HttpDownloadTests
    {
        private static readonly Uri Target = new("https://example.invalid/Killendar.exe");
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

        private sealed class FakeHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => respond(cancellationToken);
        }

        // A body whose length the response does not declare: StreamContent only knows a length for a seekable stream.
        private sealed class UndeclaredLengthStream(byte[] data) : Stream
        {
            private int _position;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = Math.Min(Math.Min(count, 1000), data.Length - _position);
                Array.Copy(data, _position, buffer, offset, n);
                _position += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // A body that sends some bytes and then never answers a read, and ignores the token as a stalled socket does.
        private sealed class StalledStream(byte[] first) : Stream
        {
            private bool _sent;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_sent) return new TaskCompletionSource<int>().Task;
                _sent = true;
                Array.Copy(first, 0, buffer, offset, first.Length);
                return Task.FromResult(first.Length);
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static HttpClient Client(Func<CancellationToken, Task<HttpResponseMessage>> respond)
            => new(new FakeHandler(respond));

        private static Task<HttpResponseMessage> Ok(HttpContent content)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        // A test that would hang on a regression fails instead.
        private static async Task Finishes(Task work)
        {
            if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20))) != work)
                Assert.Fail("The download did not end at its deadline.");
            await work;
        }

        [Fact]
        public async Task ABodyWithinTheCapIsCopiedWhole()
        {
            byte[] body = new byte[200_000];
            for (int i = 0; i < body.Length; i++) body[i] = (byte)(i % 251);
            using var http = Client(_ => Ok(new ByteArrayContent(body)));
            var output = new MemoryStream();

            await HttpDownload.CopyToAsync(http, Target, output, Generous, maximumBytes: 200_000);

            Assert.Equal(body, output.ToArray());
        }

        [Fact]
        public async Task ABodyExactlyAtTheCapIsAccepted()
        {
            using var http = Client(_ => Ok(new StreamContent(new UndeclaredLengthStream(new byte[5000]))));
            var output = new MemoryStream();

            await HttpDownload.CopyToAsync(http, Target, output, Generous, maximumBytes: 5000);

            Assert.Equal(5000, output.Length);
        }

        [Fact]
        public async Task ADeclaredLengthOverTheCapIsRefusedBeforeAnyByteIsWritten()
        {
            using var http = Client(_ => Ok(new ByteArrayContent(new byte[1001])));
            var output = new MemoryStream();

            await Assert.ThrowsAsync<HttpDownloadSizeException>(() =>
                HttpDownload.CopyToAsync(http, Target, output, Generous, maximumBytes: 1000));

            Assert.Equal(0, output.Length);
        }

        [Fact]
        public async Task AStreamedBodyThatOutrunsTheCapIsRefusedWithoutWritingPastIt()
        {
            using var http = Client(_ => Ok(new StreamContent(new UndeclaredLengthStream(new byte[5001]))));
            var output = new MemoryStream();

            await Assert.ThrowsAsync<HttpDownloadSizeException>(() =>
                HttpDownload.CopyToAsync(http, Target, output, Generous, maximumBytes: 5000));

            Assert.True(output.Length <= 5000, "Wrote " + output.Length + " bytes past a 5000 byte cap.");
        }

        [Fact]
        public async Task ACapOfZeroAcceptsAnEmptyBodyAndRefusesAnyByte()
        {
            using (var http = Client(_ => Ok(new ByteArrayContent(Array.Empty<byte>()))))
                await HttpDownload.CopyToAsync(http, Target, new MemoryStream(), Generous, maximumBytes: 0);
            using (var http = Client(_ => Ok(new StreamContent(new UndeclaredLengthStream(new byte[1])))))
                await Assert.ThrowsAsync<HttpDownloadSizeException>(() =>
                    HttpDownload.CopyToAsync(http, Target, new MemoryStream(), Generous, maximumBytes: 0));
        }

        [Fact]
        public async Task ABodyThatStallsEndsAtTheDeadline()
        {
            using var http = Client(_ => Ok(new StreamContent(new StalledStream(new byte[10]))));
            var output = new MemoryStream();
            var clock = Stopwatch.StartNew();

            await Finishes(Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                HttpDownload.CopyToAsync(http, Target, output, TimeSpan.FromMilliseconds(400))));

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "Took " + clock.Elapsed);
            Assert.Equal(10, output.Length);
        }

        [Fact]
        public async Task AServerThatNeverAnswersEndsAtTheDeadline()
        {
            using var http = Client(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            await Finishes(Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                HttpDownload.CopyToAsync(http, Target, new MemoryStream(), TimeSpan.FromMilliseconds(400))));
        }

        [Fact]
        public async Task TheCallersTokenCancelsTheDownload()
        {
            using var http = Client(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var cancel = new CancellationTokenSource();
            cancel.CancelAfter(200);

            await Finishes(Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                HttpDownload.CopyToAsync(http, Target, new MemoryStream(), Generous, cancellationToken: cancel.Token)));
        }

        [Fact]
        public async Task AFailureStatusIsAnErrorAndWritesNothing()
        {
            using var http = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
            var output = new MemoryStream();

            await Assert.ThrowsAsync<HttpRequestException>(() => HttpDownload.CopyToAsync(http, Target, output, Generous));

            Assert.Equal(0, output.Length);
        }

        [Fact]
        public async Task ANegativeCapIsRefused()
        {
            using var http = Client(_ => Ok(new ByteArrayContent(new byte[1])));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                HttpDownload.CopyToAsync(http, Target, new MemoryStream(), Generous, maximumBytes: -1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task ADeadlineThatIsNotPositiveIsRefused(int seconds)
        {
            using var http = Client(_ => Ok(new ByteArrayContent(new byte[1])));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                HttpDownload.CopyToAsync(http, Target, new MemoryStream(), TimeSpan.FromSeconds(seconds)));
        }
    }
}
