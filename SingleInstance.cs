using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

// One Killendar per desktop session. Ported from KillerNotes, for the same reason it exists
// there: two processes with the same .kcal open are two SQLite writers on one file. SQLite
// allows it, the user never notices the double launch, and the password-change file swap then
// fails with "in use by another process" (KillerNotes issue #3).
//
// A second launch forwards its command line - a double-clicked .kcal, or nothing at all - to the
// running window through a named pipe and exits. The running window comes to the front and
// handles the file exactly as a first-launch double-click would.
//
// Both the mutex and the pipe are scoped to the desktop session ("Local\" and the session id), so
// a terminal server or a fast-user-switching box still gets one instance PER USER rather than one
// for the whole machine.
namespace Killendar
{
    public partial class App
    {
        // Held for the process lifetime; the OS releases it on exit or on a crash.
        private static Mutex? _instanceMutex;

        private static string PipeName =>
            "Killendar-" + Process.GetCurrentProcess().SessionId;

        /// <summary>Claims the single-instance slot. Returns false when another instance already
        /// holds it, having first handed it our command line - the caller should then shut down.</summary>
        private bool ClaimSingleInstance()
        {
            _instanceMutex = new Mutex(true, @"Local\Killendar-SingleInstance", out bool firstInstance);
            if (!firstInstance)
            {
                ForwardToRunningInstance(PendingOpenFile);
                return false;
            }
            StartPipeServer();
            return true;
        }

        /// <summary>Second launch: hands the double-clicked path (or an empty line, meaning "just
        /// come to the front") to the running instance. Best-effort - if the pipe is unreachable
        /// because the running instance is mid-shutdown, the launch simply ends.</summary>
        private static void ForwardToRunningInstance(string? path)
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
                        Dispatcher.BeginInvoke(new Action(() => OnForwardedLaunch(path)));
                    }
                    catch (IOException) { /* client vanished mid-handshake - keep listening */ }
                    catch (ObjectDisposedException) { return; }
                }
            })
            { IsBackground = true, Name = "Killendar single-instance pipe" };
            thread.Start();
        }

        /// <summary>UI thread: brings the window forward and routes a forwarded .kcal through the
        /// same path as a first-launch double-click.</summary>
        private void OnForwardedLaunch(string? path)
        {
            if (!(MainWindow is Killendar.MainWindow win)) return;

            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Activate();
            win.Topmost = true; win.Topmost = false;   // foreground nudge past the focus rules

            if (string.IsNullOrEmpty(path)) return;
            CaptureOpenFileArgument(new[] { path! });
            if (PendingOpenFile != null) win.HandlePendingOpenFile();
        }
    }
}
