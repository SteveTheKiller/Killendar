using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Killendar.Services
{
    /// <summary>
    /// Keeps the single-exe build self-sufficient for SQLCipher. The native e_sqlcipher.dll (x64)
    /// is embedded as a resource and self-extracted to a per-version cache on first use. Must run
    /// immediately before SQLitePCL.raw.SetProvider(new SQLite3Provider_e_sqlcipher()) in the
    /// store's static constructor. Thread-safe.
    ///
    /// Do not swap the static provider for the bundle, and do not call Batteries_V2.Init(): the
    /// bundle's loader probes Assembly.Location, which is empty under Costura, and crashes at
    /// startup.
    ///
    /// The cache stays under LOCALAPPDATA even though Killendar data roams in APPDATA - an
    /// extracted x64 native is machine state, not user state, and roaming it would be wrong.
    /// </summary>
    internal static class SqlCipherBootstrap
    {
        private const string ResourceName = "Killendar.SqlCipherNative.e_sqlcipher.dll";

        private static readonly object _gate = new();
        private static volatile bool _ready;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        /// <summary>
        /// Extracts the embedded native when the cached content differs
        /// and preloads it, so the SQLitePCLRaw provider's LoadLibrary("e_sqlcipher") resolves to
        /// this module. Natives live in a per-version cache because they must match the app.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_ready) return;
            lock (_gate)
            {
                if (_ready) return;

                var asm = typeof(SqlCipherBootstrap).Assembly;
                string version = asm.GetName().Version?.ToString() ?? "0";
                string nativeDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Killendar", "native", version);
                string target = Path.Combine(nativeDir, "e_sqlcipher.dll");

                using var src = asm.GetManifestResourceStream(ResourceName)
                    ?? throw new InvalidOperationException("The embedded SQLCipher native library is missing.");
                using var embedded = new MemoryStream();
                src.CopyTo(embedded);
                PublishVerifiedCache(target, embedded.ToArray());
                if (!SetDllDirectory(nativeDir))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Could not configure the SQLCipher native library path.");
                if (LoadLibrary(target) == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Could not load the SQLCipher native library: " + target);
                _ready = true;
            }
        }

        internal static void PublishVerifiedCache(string target, byte[] contents)
        {
            byte[] expectedHash;
            using (var hash = SHA256.Create()) expectedHash = hash.ComputeHash(contents);
            string directory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(directory);
            if (!CachedMatches(target, expectedHash))
            {
                string temp = Path.Combine(directory, "e_sqlcipher." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        destination.Write(contents, 0, contents.Length);
                        destination.Flush(true);
                    }
                    if (!CachedMatches(target, expectedHash))
                    {
                        try
                        {
                            if (File.Exists(target)) File.Replace(temp, target, null);
                            else File.Move(temp, target);
                        }
                        catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) &&
                            CachedMatches(target, expectedHash)) { }
                    }
                }
                finally
                {
                    try { File.Delete(temp); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            if (!CachedMatches(target, expectedHash))
                throw new IOException("The cached SQLCipher native library differs from the embedded copy: " + target);
        }

        private static bool CachedMatches(string target, byte[] expectedHash)
        {
            try
            {
                using var cached = new FileStream(target, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var hash = SHA256.Create();
                return hash.ComputeHash(cached).SequenceEqual(expectedHash);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
