using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Killendar.Services
{
    /// <summary>
    /// Keeps the single-exe build self-sufficient for SQLCipher. The native e_sqlcipher.dll (x64)
    /// is embedded as a resource and self-extracted to a per-version cache on first use, the same
    /// pattern KillerNotes uses (and KillerPDF for its OCR natives). Must run immediately before
    /// SQLitePCL.raw.SetProvider(new SQLite3Provider_e_sqlcipher()) in the store's static
    /// constructor. Thread-safe.
    ///
    /// Ported verbatim from KillerNotes apart from the app name. Do not swap the static provider
    /// for the bundle, and do not call Batteries_V2.Init(): the bundle's loader probes
    /// Assembly.Location, which is empty under Costura, and crashes at startup.
    ///
    /// Verified 2026-07-29 from a copy of the single exe alone in an empty directory with the
    /// native cache deleted: cold extract, encrypted create, keyed roundtrip, and rejection of
    /// both a wrong password and no password all pass.
    ///
    /// The cache stays under LOCALAPPDATA even though Killendar data roams in APPDATA - an
    /// extracted x64 native is machine state, not user state, and roaming it would be wrong.
    /// </summary>
    internal static class SqlCipherBootstrap
    {
        private const string ResourceName = "Killendar.SqlCipherNative.e_sqlcipher.dll";

        private static readonly object _gate = new object();
        private static bool _ready;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        /// <summary>
        /// Extracts the embedded native (skipped when the cached copy already matches by length)
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

                try
                {
                    Directory.CreateDirectory(nativeDir);
                    using var src = asm.GetManifestResourceStream(ResourceName);
                    if (src != null &&
                        (!File.Exists(target) || new FileInfo(target).Length != src.Length))
                    {
                        // Write to a temp name then swap, so a crash mid-extract never leaves a
                        // half-written dll behind for the next launch to load.
                        string tmp = target + ".tmp";
                        using (var dst = File.Create(tmp))
                            src.CopyTo(dst);
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(tmp, target);
                    }
                }
                catch (IOException)
                {
                    // Another instance may be mid-extract or holding the dll; if the file exists
                    // in any complete form the preload below still works.
                }

                if (File.Exists(target))
                {
                    SetDllDirectory(nativeDir);
                    LoadLibrary(target);
                }

                _ready = true;
            }
        }
    }
}
