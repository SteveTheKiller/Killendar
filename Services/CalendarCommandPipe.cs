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

        internal const int MaxRequestChars = 16384;

        /// <summary>UTF-8 needs at most four bytes per character, so this byte ceiling can never
        /// refuse a request the character limit would accept.</summary>
        internal const int MaxRequestBytes = MaxRequestChars * 4;

        /// <summary>One line, read in small chunks against the byte ceiling. The deadline bounds
        /// how long a client may take; the ceiling bounds how much it may send in that time,
        /// checked as the bytes arrive rather than after a whole line has been buffered.</summary>
        internal static string? ReadRequest(Stream stream, TimeSpan timeout)
        {
            Task<string?> read = ReadLineAsync(stream);
            Within(stream, read, timeout).GetAwaiter().GetResult();
            return read.GetAwaiter().GetResult();
        }

        private static async Task<string?> ReadLineAsync(Stream stream)
        {
            var line = new MemoryStream();
            var chunk = new byte[1024];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false);
                if (read == 0) return line.Length == 0 ? null : Decode(line);
                int newline = Array.IndexOf(chunk, (byte)'\n', 0, read);
                int take = newline < 0 ? read : newline;
                if (line.Length + take > MaxRequestBytes)
                    throw new InvalidDataException("Calendar command exceeds " + MaxRequestBytes + " bytes.");
                line.Write(chunk, 0, take);
                if (newline >= 0) return Decode(line);
            }
        }

        private static string Decode(MemoryStream line) =>
            Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');

        /// <summary>One connection: read a request, answer it. An oversized or unreadable request
        /// gets an error reply, and the next connection starts clean.</summary>
        internal static void Serve(Stream pipe, Func<string, string> execute)
        {
            string response;
            try
            {
                var request = ReadRequest(pipe, ExchangeTimeout);
                response = request == null || request.Length > MaxRequestChars
                    ? InvalidCommand
                    : execute(request);
            }
            catch (InvalidDataException) { response = InvalidCommand; }
            WriteResponse(pipe, response, ExchangeTimeout);
        }

        private const string InvalidCommand = "{\"error\":\"Invalid calendar command\"}";

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
                        Serve(pipe, request => dispatcher.Invoke(() => execute(request)));
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
