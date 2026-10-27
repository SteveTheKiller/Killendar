using System.Windows;

namespace Killendar.Shell
{
    /// <summary>
    /// Window wiring for the canonical radio-row theme ContextMenu. MainWindow.xaml supplies
    /// ThemeButton, ThemeMenu and ContentPane; no Popup or duplicate flyout chrome is permitted.
    /// </summary>
    public partial class MainWindow
    {
        private Controls.ThemePicker _themePicker = null!;

        private void InitThemePicker()
        {
            _themePicker = new Controls.ThemePicker(this, ThemeMenu, ThemeButton, ContentPane);
            Services.ThemeManager.ThemeChanged += () => _themePicker.Refresh();
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e) => _themePicker.Toggle();

        /// <summary>
        /// Alt+Shift+G, the family Film Grain chord. Call from the window's PreviewKeyDown ahead of
        /// its own chain and return when it answers true. Alt chords arrive as Key.System with the
        /// real key in SystemKey. A text field never claims an Alt chord, so this works while typing.
        /// The F1 table lists it on the Alt+Shift layer (Shell/ShortcutsOverlay.cs, KsLayer.AltShift).
        /// </summary>
        private bool HandleFilmGrainKey(System.Windows.Input.KeyEventArgs e)
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (key != System.Windows.Input.Key.G ||
                System.Windows.Input.Keyboard.Modifiers !=
                (System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Shift)) return false;
            if (!e.IsRepeat) _themePicker.ToggleFilmGrain();   // Controls/ThemePicker.cs
            e.Handled = true;
            return true;
        }
    }
}
