using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Killendar.Services
{
    public static class CalendarCommandPipe
    {
        internal static bool WriteResponse(Stream stream, string response, TimeSpan timeout)
            => WriteResponseAsync(stream, response, timeout).GetAwaiter().GetResult();

        private static async Task<bool> WriteResponseAsync(Stream stream, string response, TimeSpan timeout)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(response + "\n");
            try
            {
                await Within(stream, stream.WriteAsync(bytes, 0, bytes.Length), timeout).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is TimeoutException)
            { return false; }
        }

        private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(10);

        private static async Task Within(Stream stream, Task operation, TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource();
            if (await Task.WhenAny(operation, Task.Delay(timeout, deadline.Token)).ConfigureAwait(false) == operation)
            {
                deadline.Cancel();
                await operation.ConfigureAwait(false);
                return;
            }
            stream.Dispose();
            try { await operation.ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException) { }
            throw new TimeoutException("Calendar command exchange timed out.");
        }

        internal static string? ReadRequest(Stream stream, TimeSpan timeout)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
            Task<string?> read = reader.ReadLineAsync();
            Within(stream, read, timeout).GetAwaiter().GetResult();
            return read.GetAwaiter().GetResult();
        }

        private static string PipeName => "Killendar-" + Process.GetCurrentProcess().SessionId + "-CalendarCommands";

        public static string Send(string request)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe.Connect(2000);
                if (!WriteResponse(pipe, request, ExchangeTimeout)) throw new IOException("Calendar command could not be sent.");
                return ReadRequest(pipe, ExchangeTimeout) ?? throw new IOException("Killendar did not answer the calendar command.");
            }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException)
            {
                throw new InvalidOperationException("Open and unlock Killendar before creating appointments through KillerMCP.", ex);
            }
        }

        internal static void Start(Dispatcher dispatcher, Func<string, string> execute)
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        pipe.WaitForConnection();
                        var request = ReadRequest(pipe, ExchangeTimeout);
                        var response = request == null || request.Length > 16384
                            ? "{\"error\":\"Invalid calendar command\"}"
                            : dispatcher.Invoke(() => execute(request));
                        WriteResponse(pipe, response, ExchangeTimeout);
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { return; }
                    catch (Exception) { }
                }
            }) { IsBackground = true, Name = "Killendar calendar command pipe" };
            thread.Start();
        }
    }
}
