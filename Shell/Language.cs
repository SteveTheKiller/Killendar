using System.Windows;
using Killendar.Services;

// The language menu's window half: the handler the XAML binds to, and putting the strings that were
// set from code back after a live language change. MainWindow.xaml names LangButton (a RailButton)
// and its LangMenu ContextMenu; ContentPane is the same flyout boundary the theme picker uses.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private Controls.LanguageMenu _languageMenu = null!;

        /// <summary>Call from the constructor after InitThemePicker.</summary>
        private void InitLanguageMenu()
        {
            _languageMenu = new Controls.LanguageMenu(LangMenu, LangButton, ContentPane, RelocalizeDynamicUi);
            // Translator test mode (--lang-file): a saved edit re-applies the strings, so rebuild
            // the code-built ones the same way a language switch does.
            LocaleManager.ExternalReloaded += RelocalizeDynamicUi;
            // The event is static, so a closed window would stay subscribed and alive. An app
            // with several windows opens and closes many.
            Closed += (_, _) => LocaleManager.ExternalReloaded -= RelocalizeDynamicUi;
        }

        private void LangButton_Click(object sender, RoutedEventArgs e) => _languageMenu.Open();

        /// <summary>
        /// Re-applies strings that were set from code, so a live language switch updates them.
        /// {DynamicResource Str_*} in XAML refreshes itself; this is the remainder. The app adds its
        /// own code-built text here (views it builds, tooltips that track state).
        /// </summary>
        private void RelocalizeDynamicUi()
        {
            // Toolbar captions and its right-click menu are built in code (ToolbarStyle.cs).
            RelocalizeToolbar();

            // The F1 overlay is built from the table, so an open one is rebuilt in the new language.
            if (ShortcutsOverlay.Visibility == Visibility.Visible) ApplyShortcutView(_ksMapView);

            // The status line is transient; put it back to a neutral, translated idle rather than
            // leaving the previous language's sentence sitting there.
            StatusText.Text = LocaleManager.Loc("Str_Status_Ready");

            RelocalizeAppUi();
        }

        /// <summary>App hook: re-apply the app's own code-built text (views it builds, tooltips
        /// that track state) after a language switch, from an app file, so this one stays the
        /// kit's.</summary>
        partial void RelocalizeAppUi();
    }
}
