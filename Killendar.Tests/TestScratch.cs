using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace Killendar.Tests
{
    /// <summary>
    /// Owned scratch space for the suite. KILLENDAR_TEST_ROOT picks the folder (the default is
    /// %TEMP%\killendar-tests), and every run gets its own product profile under it, so settings,
    /// the SQLCipher native cache and default Killendars never touch the real user profile. The
    /// profile is set before any product type loads and child fixture processes inherit it.
    /// </summary>
    internal static class TestScratch
    {
        internal static readonly string Root = ResolveRoot();

        internal static readonly string Profile = Path.Combine(Root, "profile",
            Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N"));

        [ModuleInitializer]
        internal static void Isolate()
        {
            Directory.CreateDirectory(Profile);
            Environment.SetEnvironmentVariable(AppPaths.ProfileRootVariable, Profile);
        }

        /// <summary>A marker a child process writes, read once it is written. File.Exists turns true
        /// the moment the child creates the file, while it may still hold it open to write, so a
        /// read straight after Exists can hit a sharing violation. Null if nothing readable
        /// appears within <paramref name="timeoutMs"/>.</summary>
        internal static string? ReadMarker(string path, int timeoutMs = 10000)
        {
            string? text = null;
            System.Threading.SpinWait.SpinUntil(() =>
            {
                try { text = File.ReadAllText(path); return text.Length > 0; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }, timeoutMs);
            return text;
        }

        private static string ResolveRoot()
        {
            string? configured = Environment.GetEnvironmentVariable("KILLENDAR_TEST_ROOT");
            return Path.GetFullPath(!string.IsNullOrWhiteSpace(configured)
                ? configured!
                : Path.Combine(Path.GetTempPath(), "killendar-tests"));
        }
    }
}
