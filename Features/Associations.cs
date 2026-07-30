using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

// Windows shell integration for .kcal, plus the single-instance plumbing that makes
// double-clicking one safe.
//
// This is much simpler than KillerShell's Associations.cs, and deliberately so: Killendar OWNS
// .kcal. There is no existing default action to displace, no UserChoice hash to forge, and no
// question of whether taking the open verb changes what a double-click DOES. So it registers
// itself the way KillerNotes does for .kndb - HKCU only, no elevation, best-effort, on every
// launch so the association follows the exe if it moves - rather than hiding behind an opt-in
// card.
//
// Single instance matters more here than it looks. Two Killendar processes with the same .kcal
// open are two SQLite writers on one file: SQLite allows it, the user never notices, and the
// password-change file swap then fails with "in use by another process" (KillerNotes issue #3).
// A second launch forwards its command line to the running window through a named pipe and exits.
namespace Killendar
{
    public partial class App
    {
        internal const string KcalProgId  = "Killendar.Killendar";
        internal const string KcalDisplay = "Killendar";

        /// <summary>Path of a double-clicked .kcal, waiting for the window to finish opening.
        /// Internal so MainWindow can drain it, and so a forwarded second launch can refill it.</summary>
        internal static string? PendingOpenFile;

        private static string ExePath => Process.GetCurrentProcess().MainModule!.FileName;

        // ============================================================
        // File association
        // ============================================================

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        /// <summary>Registers .kcal under HKCU. Best-effort: a failure costs the double-click
        /// convenience, never the ability to open a Killendar from inside the app.</summary>
        internal static void RegisterFileAssociations()
        {
            try
            {
                string exe = ExePath;

                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Services.EventStore.Extension))
                    k.SetValue("", KcalProgId);
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + KcalProgId))
                {
                    k.SetValue("", KcalDisplay);
                    k.SetValue("FriendlyTypeName", KcalDisplay);
                }
                // The exe's own icon rather than a dedicated .kcal icon: Explorer's DefaultIcon
                // needs a real file path, and extracting a second .ico buys nothing until there is
                // artwork that actually differs from the app icon.
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + KcalProgId + @"\DefaultIcon"))
                    k.SetValue("", exe + ",0");
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + KcalProgId + @"\shell\open"))
                    k.SetValue("FriendlyAppName", KcalDisplay);
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + KcalProgId + @"\shell\open\command"))
                    k.SetValue("", "\"" + exe + "\" \"%1\"");

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { /* best-effort */ }
        }

        /// <summary>Takes it back out, from both uninstall paths. A ProgID pointing at a deleted
        /// exe is how you get a broken Open With list that nothing in the UI will offer to clean
        /// up.</summary>
        internal static void UnregisterFileAssociations()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Classes\" + KcalProgId, throwOnMissingSubKey: false);
                using (var k = Registry.CurrentUser.OpenSubKey(
                           @"Software\Classes\" + Services.EventStore.Extension, writable: true))
                {
                    // Only drop the default if it is still ours - another app may have taken it.
                    if (k != null && (k.GetValue("") as string) == KcalProgId)
                        k.SetValue("", "");
                }
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { /* best-effort */ }
        }

        /// <summary>Captures a .kcal handed to us on the command line. Ignores anything else, so a
        /// stray argument can never be mistaken for a calendar.</summary>
        internal static void CaptureOpenFileArgument(string[] args)
        {
            if (args.Length == 0) return;
            string path = args[0];
            if (!File.Exists(path)) return;
            if (!string.Equals(Path.GetExtension(path), Services.EventStore.Extension,
                               StringComparison.OrdinalIgnoreCase)) return;
            PendingOpenFile = path;
        }
    }
}
