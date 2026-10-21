using System;
using System.IO;

namespace Killendar
{
    /// <summary>
    /// The two per-user folders Killendar keeps its own state in: LOCALAPPDATA for machine-bound
    /// state (settings, the SQLCipher native cache, the crash log) and APPDATA for the default
    /// Killendars folder. KILLENDAR_PROFILE_ROOT moves both under one folder, as Local and Roaming,
    /// so the test suite and diagnostic runs never touch the real profile.
    ///
    /// Install locations are deliberately not redirected: they describe where Windows put the app,
    /// not where the app keeps its state.
    /// </summary>
    internal static class AppPaths
    {
        internal const string ProfileRootVariable = "KILLENDAR_PROFILE_ROOT";

        internal static string LocalData =>
            Resolve(Environment.GetEnvironmentVariable(ProfileRootVariable), "Local",
                Environment.SpecialFolder.LocalApplicationData);

        internal static string RoamingData =>
            Resolve(Environment.GetEnvironmentVariable(ProfileRootVariable), "Roaming",
                Environment.SpecialFolder.ApplicationData);

        /// <summary>A blank or relative root is ignored rather than resolved against whatever the
        /// current directory happens to be.</summary>
        internal static string Resolve(string? root, string name, Environment.SpecialFolder folder) =>
            !string.IsNullOrWhiteSpace(root) && Path.IsPathRooted(root)
                ? Path.Combine(Path.GetFullPath(root), name)
                : Environment.GetFolderPath(folder);
    }
}
