using System.Windows;
using Killendar.Services;

namespace Killendar.Shell
{
    /// <summary>
    /// The language menu's window half: the handler the XAML binds to, and putting the strings that
    /// were set from code back after a live language change.
    /// </summary>
    public partial class MainWindow
    {
        private Controls.LanguageMenu _languageMenu = null!;

        /// <summary>Builds the rail menu over the button's own ContextMenu.</summary>
        private void InitLanguageMenu()
        {
            _languageMenu = new Controls.LanguageMenu(
                // LangButton, not RailFlyoutAnchor: a flyout hangs off its own button (family rule,
                // see the ThemePopup note in MainWindow.xaml).
                LangMenu, LangButton, RelocalizeDynamicUi, ReformatOpenEditor);
        }

        private void LangButton_Click(object sender, RoutedEventArgs e) => _languageMenu.Open();

        /// <summary>Look up a localized string.</summary>
        private string Loc(string key) => LocaleManager.Loc(key);

        /// <summary>An open editor is showing dates in the old pattern, so reformat what is in the
        /// fields rather than leaving a mix of the two.</summary>
        private void ReformatOpenEditor()
        {
            if (_sidebarOpen) _appointments.ReformatDates();
        }

        /// <summary>
        /// Re-applies strings that were set from code, so a live language switch updates them.
        /// Static {DynamicResource Str_*} in XAML refreshes itself; this is the remainder -
        /// anything a handler assigned to .Text or .Content, plus the code-built calendar views.
        /// </summary>
        private void RelocalizeDynamicUi()
        {
            // The views are rebuilt wholesale, which re-reads every string they use.
            _calendar.Refresh();

            // Rail tooltips track the panel state.
            SidebarToggleBtn.ToolTip = Loc(_sidebarOpen ? "Str_TT_PanelHide" : "Str_TT_PanelShow");

            // The panel's own dynamic bits: heading and the all-day toggle.
            _appointments.RefreshLocalizedText();

            // Toolbar captions and its right-click menu are built in code, so they do not follow
            // a DynamicResource on their own.
            RelocalizeToolbar();

            // The status line is transient by nature; put it back to a neutral, translated idle
            // rather than leaving the previous language's sentence sitting there.
            StatusText.Text = Loc("Str_Status_Ready");
        }
    }
}
