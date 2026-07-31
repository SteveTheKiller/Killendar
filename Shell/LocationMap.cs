using System;
using System.Diagnostics;
using System.Windows;

// ============================================================
// The map button beside the appointment's LOCATION field.
//
// This is deliberately the DUMBEST possible version of "look up an address", and that is the
// feature, not a shortcut. killendar.net's headline is "no account, no sync, and nothing
// phoning home" and the About card says appointments never leave your machine. A geocoder
// type-ahead would send every keystroke in this box to a third party for every appointment the
// user edits, quietly contradicting all of that.
//
// So: no API key, no dependency, no request. The text is handed to the user's default browser
// as a maps search URL and the app's involvement ends there. Nothing leaves the machine until
// somebody deliberately clicks this button.
//
// An opt-in Photon (Komoot) type-ahead is the planned second half - off by default, with the
// switch on the About card next to the signature and the exe hash, which is where "may this
// talk to the network" belongs. See BACKLOG.md in the folder above the repos.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        /// <summary>
        /// google.com/maps/search/ with the typed text as the query. Universal cross-platform
        /// URL form; Windows hands it to whatever the user has registered for https, so someone
        /// with a maps app installed gets that instead of the website.
        /// </summary>
        private const string MapsSearchUrl = "https://www.google.com/maps/search/?api=1&query=";

        private void LocationMap_Click(object sender, RoutedEventArgs e)
        {
            var query = FieldLocation.Text.Trim();
            if (query.Length == 0) return;   // nothing typed, nothing to look up

            try
            {
                // UseShellExecute is the default on net48, but say it: without it a URL is
                // treated as a file path and throws.
                Process.Start(new ProcessStartInfo(MapsSearchUrl + Uri.EscapeDataString(query))
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception)
            {
                // No browser registered, or the shell refused. Not worth a dialog over - the
                // address is still sitting in the field where the user typed it.
            }
        }
    }
}
