using System.Windows;
using Killendar.Features.About;

namespace Killendar.Shell
{
    /// <summary>
    /// APP-OWNED: the rows Killendar's About card adds to the shared core (Shell/About.cs): Start with
    /// Windows, Close to tray and Copy diagnostics, plus the frame dimming behind an open overlay.
    /// Kept out of the kit drift check (KillerHarness family/kit-owned.json).
    /// </summary>
    public partial class MainWindow : IAboutStartup, IAboutTray
    {
        bool IAboutStartup.StartWithWindows { set => AboutStartWithWindows.IsChecked = value; }
        bool IAboutTray.CloseToTray         { set => AboutCloseToTray.IsChecked = value; }

        private void AboutStartWithWindows_Click(object sender, RoutedEventArgs e)
            => _about.SetStartWithWindows(AboutStartWithWindows.IsChecked == true);

        private void AboutCloseToTray_Click(object sender, RoutedEventArgs e)
            => _about.SetCloseToTray(AboutCloseToTray.IsChecked == true);

        private void AboutDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(Services.Diagnostics.Build(_store));
                StatusText.Text = Loc("Str_About_DiagnosticsCopied");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                StatusText.Text = Loc("Str_About_DiagnosticsFailed");   // another program is holding the clipboard
            }
        }

        // The classic frame dims behind an open overlay and comes back when it closes.
        partial void OnOverlayShowing(UIElement overlay) => ClassicFrameChrome.Opacity = 0.42;
        partial void OnOverlayHidden(UIElement overlay) => ClassicFrameChrome.Opacity = 1;
    }
}
