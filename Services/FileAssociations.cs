using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Killendar.Services
{
    /// <summary>
    /// Windows shell integration for the calendar extension.
    ///
    /// Killendar owns that extension: there is no existing default action to displace, no UserChoice
    /// hash to forge, and no question of whether taking the open verb changes what a double-click
    /// does. So it registers under HKCU only, without elevation, best-effort, on every launch, so
    /// the association follows the exe if it moves.
    /// </summary>
    internal static class FileAssociations
    {
        internal const string ProgId      = "Killendar.Killendar";
        internal const string DisplayName = "Killendar";

        private static string ExePath => Process.GetCurrentProcess().MainModule!.FileName;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        /// <summary>Registers the extension under HKCU. Best-effort: a failure costs the double-click
        /// convenience, never the ability to open a Killendar from inside the app.</summary>
        internal static void Register() => Register(Registry.CurrentUser, ExePath);

        internal static void Register(RegistryKey root, string exe)
        {
            try
            {
                using (var k = root.CreateSubKey(@"Software\Classes\" + EventStore.Extension))
                    k.SetValue("", ProgId);
                using (var k = root.CreateSubKey(@"Software\Classes\" + ProgId))
                {
                    k.SetValue("", DisplayName);
                    k.SetValue("FriendlyTypeName", DisplayName);
                }
                // The exe's own icon rather than a dedicated icon: Explorer's DefaultIcon needs a
                // real file path, and extracting a second .ico buys nothing until there is artwork
                // that actually differs from the app icon.
                using (var k = root.CreateSubKey(@"Software\Classes\" + ProgId + @"\DefaultIcon"))
                    k.SetValue("", exe + ",0");
                using (var k = root.CreateSubKey(@"Software\Classes\" + ProgId + @"\shell\open"))
                    k.SetValue("FriendlyAppName", DisplayName);
                using (var k = root.CreateSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
                    k.SetValue("", "\"" + exe + "\" \"%1\"");

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { /* best-effort */ }
        }

        /// <summary>Takes it back out, from both uninstall paths. A ProgID pointing at a deleted exe
        /// is how you get a broken Open With list that nothing in the UI will offer to clean up.
        /// </summary>
        internal static void Unregister() => Unregister(Registry.CurrentUser);

        internal static void Unregister(RegistryKey root)
        {
            try
            {
                root.DeleteSubKeyTree(
                    @"Software\Classes\" + ProgId, throwOnMissingSubKey: false);
                using (var k = root.OpenSubKey(
                           @"Software\Classes\" + EventStore.Extension, writable: true))
                {
                    // Only drop the default if it is still ours - another app may have taken it.
                    if (k != null && (k.GetValue("") as string) == ProgId)
                        k.SetValue("", "");
                }
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { /* best-effort */ }
        }

        /// <summary>The calendar file handed to us on the command line, or null. Anything else is
        /// ignored, so a stray argument can never be mistaken for a calendar.</summary>
        internal static string? CalendarPathFrom(string[] args)
        {
            if (args.Length == 0) return null;
            string path = args[0];
            if (!File.Exists(path)) return null;
            if (!string.Equals(Path.GetExtension(path), EventStore.Extension,
                               StringComparison.OrdinalIgnoreCase)) return null;
            return path;
        }
    }
}
