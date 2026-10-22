using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public class CalendarPipeDeadlineTests
    {
        [Fact]
        public async Task StalledReaderIsDroppedAndNextClientReceivesItsReply()
        {
            string name = "Killendar-test-" + Guid.NewGuid().ToString("N");
            using var firstServer = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096);
            using var stalled = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            var connect = firstServer.WaitForConnectionAsync();
            stalled.Connect(10000);
            await connect;
            var write = Task.Run(() => CalendarCommandPipe.WriteResponse(firstServer,
                new string('x', 8 * 1024 * 1024), TimeSpan.FromMilliseconds(250)));
            try
            {
                Assert.Same(write, await Task.WhenAny(write, Task.Delay(10000)));
                Assert.False(await write);
                firstServer.Dispose();
                using var nextServer = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using var next = new NamedPipeClientStream(".", name, PipeDirection.InOut);
                var nextConnect = nextServer.WaitForConnectionAsync();
                next.Connect(10000);
                await nextConnect;
                using var reader = new StreamReader(next);
                var response = reader.ReadLineAsync();
                Assert.True(CalendarCommandPipe.WriteResponse(nextServer, "{\"ok\":true}", TimeSpan.FromSeconds(10)));
                Assert.Equal("{\"ok\":true}", await response);
            }
            finally
            {
                stalled.Dispose();
                firstServer.Dispose();
                try { await write; } catch (IOException) { } catch (ObjectDisposedException) { }
            }
        }

        [Fact]
        public async Task ClientThatNeverFinishesItsRequestTimesOut()
        {
            string name = "Killendar-test-" + Guid.NewGuid().ToString("N");
            using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            var connect = server.WaitForConnectionAsync();
            client.Connect(10000);
            await connect;
            var read = Task.Run(() => Assert.Throws<TimeoutException>(() =>
                CalendarCommandPipe.ReadRequest(server, TimeSpan.FromMilliseconds(250))));
            try { Assert.Same(read, await Task.WhenAny(read, Task.Delay(2000))); }
            finally { client.Dispose(); server.Dispose(); await read; }
        }
    }
}
