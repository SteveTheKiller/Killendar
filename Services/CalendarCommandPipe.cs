using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
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

        public static string Send(string request) => Send(request, PipeName, null);

        internal static string Send(string request, string pipeName,
            Func<PipeStream, SecurityIdentifier?>? serverOwner)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe.Connect(2000);
                PipeGuard.RequireCurrentUserServer(pipe, serverOwner);
                if (!WriteResponse(pipe, request, ExchangeTimeout)) throw new IOException("Calendar command could not be sent.");
                return ReadRequest(pipe, ExchangeTimeout) ?? throw new IOException("Killendar did not answer the calendar command.");
            }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException)
            {
                throw new InvalidOperationException("Open and unlock Killendar before creating appointments through KillerMCP.", ex);
            }
        }

        internal static NamedPipeServerStream CreateServer(string name) =>
            PipeGuard.CreateServer(name, PipeDirection.InOut);

        internal static void Start(Dispatcher dispatcher, Func<string, string> execute)
        {
            string name = PipeName;
            var thread = new Thread(() => PipeGuard.RunServerLoop(
                () => CreateServer(name),
                pipe => Serve(pipe, request => dispatcher.Invoke(() => execute(request))),
                () => false, Thread.Sleep))
            { IsBackground = true, Name = "Killendar calendar command pipe" };
            thread.Start();
        }
    }

    /// <summary>Pipe names live in a machine-wide namespace, so another local user could pre-create
    /// one. Servers restrict access to the current user; clients refuse a server owned by anyone else.</summary>
    internal static class PipeGuard
    {
        [DllImport("kernel32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;

        /// <summary>The account running the process at the other end of a connected client pipe, or
        /// null when it cannot be determined.</summary>
        internal static SecurityIdentifier? OwnerOfServer(PipeStream pipe)
        {
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid)) return null;
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                if (!OpenProcessToken(process, TokenQuery, out IntPtr token)) return null;
                try { using var identity = new WindowsIdentity(token); return identity.User; }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(process); }
        }

        /// <summary>Refuses a connected pipe whose server is not run by the current user.</summary>
        internal static void RequireCurrentUserServer(PipeStream pipe, Func<PipeStream, SecurityIdentifier?>? lookup)
        {
            var owner = (lookup ?? OwnerOfServer)(pipe);
            if (owner == null || owner != WindowsIdentity.GetCurrent().User)
                throw new IOException("The pipe server is not running as the current user.");
        }

        internal static NamedPipeServerStream CreateServer(string name, PipeDirection direction)
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!,
                PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(name, direction, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 0, 0, security);
        }

        /// <summary>Serves one connection at a time until <paramref name="stop"/> or disposal. A failure
        /// to create the pipe (a squatter holds the name, access denied) waits 250 ms, doubling to
        /// 5 s, instead of spinning; a completed connection resets the wait.</summary>
        internal static void RunServerLoop(Func<NamedPipeServerStream> create, Action<NamedPipeServerStream> handle,
            Func<bool> stop, Action<TimeSpan> delay)
        {
            var wait = TimeSpan.Zero;
            while (!stop())
            {
                try
                {
                    using var pipe = create();
                    pipe.WaitForConnection();
                    handle(pipe);
                    wait = TimeSpan.Zero;
                }
                catch (ObjectDisposedException) { return; }
                catch (Exception)
                {
                    wait = NextWait(wait);
                    delay(wait);
                }
            }
        }

        internal static TimeSpan NextWait(TimeSpan previous) =>
            previous == TimeSpan.Zero ? TimeSpan.FromMilliseconds(250)
            : TimeSpan.FromMilliseconds(Math.Min(previous.TotalMilliseconds * 2, 5000));
    }
}
