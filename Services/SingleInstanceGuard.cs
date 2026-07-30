using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
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
    internal sealed class SingleInstanceGuard
    {
        private readonly string _id;
        private readonly Dispatcher _dispatcher;
        private readonly Action<string?> _onForwarded;

        // Held for the process lifetime; the OS releases it on exit or on a crash.
        private Mutex? _mutex;

        internal SingleInstanceGuard(string id, Dispatcher dispatcher, Action<string?> onForwarded)
        {
            _id          = id;
            _dispatcher  = dispatcher;
            _onForwarded = onForwarded;
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
                Forward(forwardPath);
                return false;
            }
            StartPipeServer();
            return true;
        }

        /// <summary>Second launch: hands the double-clicked path (or an empty line, meaning "just come
        /// to the front") to the running instance. Best-effort - if the pipe is unreachable because
        /// the running instance is mid-shutdown, the launch simply ends.</summary>
        private void Forward(string? path)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(2000);
                using var w = new StreamWriter(pipe) { AutoFlush = true };
                w.WriteLine(path ?? "");
            }
            catch { /* nothing to do */ }
        }

        /// <summary>First instance: listens for forwarded launches for the process lifetime on a
        /// background thread; each message is marshalled onto the UI thread.</summary>
        private void StartPipeServer()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                        server.WaitForConnection();
                        using var r = new StreamReader(server);
                        string? path = r.ReadLine();
                        _dispatcher.BeginInvoke(new Action(() => _onForwarded(path)));
                    }
                    catch (IOException) { /* client vanished mid-handshake - keep listening */ }
                    catch (ObjectDisposedException) { return; }
                }
            })
            { IsBackground = true, Name = _id + " single-instance pipe" };
            thread.Start();
        }
    }
}
