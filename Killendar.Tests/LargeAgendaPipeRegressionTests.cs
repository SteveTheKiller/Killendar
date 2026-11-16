using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class LargeAgendaPipeRegressionTests
    {
        [Fact]
        public async Task ValidLargeAgendaCrossesNamedPipe()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.ImportEvents(Enumerable.Range(0, 100).Select(i => new CalendarEvent
            {
                Title = new string('T', 200), Location = new string('L', 500),
                Start = new DateTime(2026, 11, 15, 9, 0, 0), End = new DateTime(2026, 11, 15, 10, 0, 0),
            }));
            const string request = "{\"command\":\"agenda\",\"date\":\"2026-11-15\",\"days\":1,\"limit\":100}";
            string response = CalendarCommands.Create(fixture.Store, request);
            Assert.True(Encoding.UTF8.GetByteCount(response) > CalendarCommandPipe.MaxRequestBytes);
            string name = "Killendar-large-agenda-" + Guid.NewGuid().ToString("N");
            using var server = CalendarCommandPipe.CreateServer(name);
            var serving = Task.Run(() =>
            {
                server.WaitForConnection();
                CalendarCommandPipe.Serve(server, _ => response);
            });
            try { Assert.Equal(response, CalendarCommandPipe.Send(request, name, null)); }
            finally
            {
                server.Dispose();
                Assert.Same(serving, await Task.WhenAny(serving, Task.Delay(TimeSpan.FromSeconds(15))));
                await serving;
            }
        }
    }
}
