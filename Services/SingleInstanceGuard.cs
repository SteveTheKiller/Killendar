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
    /// <summary>
    /// One instance per desktop session.
    ///
    /// Two processes with the same calendar file open are two SQLite writers on one file. SQLite
    /// allows it, the user never notices the double launch, and the password-change file swap then
    /// fails with "in use by another process".
    ///
    /// A second launch forwards its command line - a double-clicked calendar, or nothing at all - to
    /// the running instance through a named pipe and exits. The running instance brings its window
    /// forward and handles the file exactly as a first-launch double-click would.
    ///
    /// Both the mutex and the pipe are scoped to the desktop session, so a terminal server or a
    /// fast-user-switching box gets one instance PER USER rather than one for the whole machine.
    /// </summary>
    internal sealed class SingleInstanceGuard : IDisposable
    {
        private readonly string _id;
        private readonly Action<Action> _dispatch;
        private readonly Action<string?> _onForwarded;
        private readonly int _timeoutMilliseconds;
        private readonly object _serverGate = new();
        private NamedPipeServerStream? _server;
        private Thread? _thread;
        private volatile bool _disposed;
        private bool _ownsMutex;
        internal const int MaxPayloadCharacters = 32768;
        private const int MaxPayloadBytes = MaxPayloadCharacters * 4;
        internal bool ForwardingSucceeded { get; private set; } = true;

        // Held for the process lifetime; the OS releases it on exit or on a crash.
        private Mutex? _mutex;

        internal SingleInstanceGuard(string id, Dispatcher dispatcher, Action<string?> onForwarded)
            : this(id, action => dispatcher.BeginInvoke(action), onForwarded) { }

        internal SingleInstanceGuard(string id, Action<Action> dispatch, Action<string?> onForwarded,
            int timeoutMilliseconds = 2000)
        {
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            _id          = id;
            _dispatch = dispatch;
            _onForwarded = onForwarded;
            _timeoutMilliseconds = timeoutMilliseconds;
        }

        private string PipeName => _id + "-" + Process.GetCurrentProcess().SessionId;

        /// <summary>Claims the single-instance slot. Returns false when another instance already holds
        /// it, having first handed that instance <paramref name="forwardPath"/> - the caller should
        /// then shut down.</summary>
        internal bool Claim(string? forwardPath)
        {
            _mutex = new Mutex(true, @"Local\" + _id + "-SingleInstance", out bool firstInstance);
            if (!firstInstance)
            {
                ForwardingSucceeded = Forward(forwardPath);
                return false;
            }
            _ownsMutex = true;
            StartPipeServer();
            return true;
        }

        /// <summary>Second launch: hands the double-clicked path (or an empty line, meaning "just come
        /// to the front") to the running instance. Failure is exposed to the startup caller.</summary>
        private bool Forward(string? path)
        {
            path ??= "";
            if (path.Length > MaxPayloadCharacters || path.IndexOfAny(['\r', '\n']) >= 0) return false;
            try
            {
                byte[] payload = new UTF8Encoding(false, true).GetBytes(path + "\n");
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                pipe.Connect(_timeoutMilliseconds);
                AwaitWithin(pipe.WriteAsync(payload, 0, payload.Length), _timeoutMilliseconds);
                return true;
            }
            catch (IOException) { return false; }
            catch (TimeoutException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (EncoderFallbackException) { return false; }
        }

        /// <summary>First instance: listens for forwarded launches for the process lifetime on a
        /// background thread; each message is marshalled onto the UI thread.</summary>
        private void StartPipeServer()
        {
            var thread = new Thread(() =>
            {
                while (!_disposed)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        lock (_serverGate)
                        {
                            if (_disposed) return;
                            _server = server;
                        }
                        server.WaitForConnection();
                        string path = ReadPayload(server);
                        _dispatch(() => _onForwarded(path));
                    }
                    catch (IOException) { /* client vanished mid-handshake - keep listening */ }
                    catch (TimeoutException) { }
                    catch (DecoderFallbackException) { }
                    catch (ObjectDisposedException) when (_disposed) { return; }
                }
            })
            { IsBackground = true, Name = _id + " single-instance pipe" };
            _thread = thread;
            thread.Start();
        }

        private string ReadPayload(NamedPipeServerStream server)
        {
            var timer = Stopwatch.StartNew();
            using var payload = new MemoryStream();
            byte[] buffer = new byte[1024];
            while (true)
            {
                int remaining = _timeoutMilliseconds - (int)timer.ElapsedMilliseconds;
                var read = server.ReadAsync(buffer, 0, buffer.Length);
                AwaitWithin(read, remaining);
                int count = read.GetAwaiter().GetResult();
                if (count == 0) throw new IOException("The launch request ended before its newline.");
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        string path = new UTF8Encoding(false, true).GetString(payload.ToArray());
                        if (path.EndsWith("\r", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 1);
                        if (path.Length > MaxPayloadCharacters || path.IndexOf('\r') >= 0)
                            throw new IOException("The launch request exceeds its payload limit or contains a newline.");
                        return path;
                    }
                    if (payload.Length >= MaxPayloadBytes)
                        throw new IOException("The launch request exceeds its payload limit.");
                    payload.WriteByte(buffer[i]);
                }
            }
        }

        private static void AwaitWithin(Task operation, int milliseconds)
        {
            if (milliseconds <= 0 || Task.WhenAny(operation, Task.Delay(milliseconds)).GetAwaiter().GetResult() != operation)
            {
                _ = operation.ContinueWith(task => { var ignored = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("The launch pipe deadline expired.");
            }
            operation.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _disposed = true;
            lock (_serverGate) _server?.Dispose();
            _thread?.Join(3000);
            if (_ownsMutex) _mutex?.ReleaseMutex();
            _ownsMutex = false;
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}
