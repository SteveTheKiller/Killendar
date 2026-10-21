using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // BF-013: the pipe's size limit applies while reading, so a client streaming a request with no
    // newline cannot make Killendar buffer it until the deadline.
    public class CalendarPipeSizeRegressionTests
    {
        [Fact]
        public void NewlineFreeFloodIsRefusedAfterTheLimitNotAtTheDeadline()
        {
            using var flood = new EndlessStream();

            Assert.Throws<InvalidDataException>(() => CalendarCommandPipe.ReadRequest(flood, TimeSpan.FromSeconds(5)));

            Assert.True(flood.Served <= CalendarCommandPipe.MaxRequestBytes + 4096,
                "Read " + flood.Served + " bytes before refusing.");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(CalendarCommandPipe.MaxRequestChars)]
        public void RequestsUpToTheLimitAreRead(int length)
        {
            string request = new string('a', length);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(request + "\r\n"));

            Assert.Equal(request, CalendarCommandPipe.ReadRequest(stream, TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void RequestOverTheCharacterLimitIsAnsweredWithAnErrorAndTheNextOneWorks()
        {
            int calls = 0;
            using var tooLong = new DuplexStream(new string('a', CalendarCommandPipe.MaxRequestChars + 1) + "\n");
            CalendarCommandPipe.Serve(tooLong, request => { calls++; return "{\"ok\":true}"; });
            Assert.Contains("Invalid calendar command", tooLong.Response);

            using var flood = new DuplexStream(null);
            CalendarCommandPipe.Serve(flood, request => { calls++; return "{\"ok\":true}"; });
            Assert.Contains("Invalid calendar command", flood.Response);

            using var valid = new DuplexStream("{\"title\":\"x\"}\n");
            CalendarCommandPipe.Serve(valid, request => { calls++; return "{\"ok\":true}"; });
            Assert.Equal("{\"ok\":true}\n", valid.Response);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void MultibyteRequestIsDecodedWhole()
        {
            string request = "{\"title\":\"Café 測試\"}";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(request + "\n"));

            Assert.Equal(request, CalendarCommandPipe.ReadRequest(stream, TimeSpan.FromSeconds(5)));
        }

        /// <summary>Serves 'a' forever and counts what it served.</summary>
        private sealed class EndlessStream : Stream
        {
            internal long Served;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Served; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                for (int i = 0; i < count; i++) buffer[offset + i] = (byte)'a';
                Interlocked.Add(ref Served, count);
                return count;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>Reads a fixed request (or floods when null) and records what was written back.</summary>
        private sealed class DuplexStream : Stream
        {
            private readonly Stream _input;
            private readonly MemoryStream _output = new();
            internal DuplexStream(string? request) =>
                _input = request == null ? new EndlessStream() : new MemoryStream(Encoding.UTF8.GetBytes(request));
            internal string Response => Encoding.UTF8.GetString(_output.ToArray());
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
            {
                _output.Write(buffer, offset, count);
                return Task.CompletedTask;
            }
            protected override void Dispose(bool disposing) { if (disposing) _input.Dispose(); base.Dispose(disposing); }
        }
    }
}
