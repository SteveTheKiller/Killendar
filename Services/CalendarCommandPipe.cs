using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace Killendar.Services
{
    public static class CalendarCommandPipe
    {
        private static string PipeName => "Killendar-" + Process.GetCurrentProcess().SessionId + "-CalendarCommands";

        public static string Send(string request)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                pipe.Connect(2000);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                writer.WriteLine(request);
                return reader.ReadLine() ?? throw new IOException("Killendar did not answer the calendar command.");
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
                            PipeTransmissionMode.Byte, PipeOptions.None);
                        pipe.WaitForConnection();
                        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                        var request = reader.ReadLine();
                        var response = request == null || request.Length > 16384
                            ? "{\"error\":\"Invalid calendar command\"}"
                            : dispatcher.Invoke(() => execute(request));
                        writer.WriteLine(response);
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
