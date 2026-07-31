using System.Windows;

namespace Killendar.Shell
{
    /// <summary>
    /// The theme flyout's window half: the handlers the XAML binds to, forwarded to ThemePicker.
    /// </summary>
    public partial class MainWindow
    {
        private Controls.ThemePicker _themePicker = null!;

        /// <summary>Builds the picker over the flyout's elements and seeds the selection rings, then
        /// keeps them in step with a theme changed from anywhere else.</summary>
        private void InitThemePicker()
        {
            _themePicker = new Controls.ThemePicker(
                this, ThemeMenu, ThemeButton, ThemeSwatches, AccentSwatches, AccentLabel);
            _themePicker.Refresh();
            Services.ThemeManager.ThemeChanged += () => _themePicker.Refresh();
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e) => _themePicker.Toggle();

        private void ThemeSwatch_Click(object sender, RoutedEventArgs e) =>
            _themePicker.PickTheme((sender as FrameworkElement)?.Tag);

        private void AccentSwatch_Click(object sender, RoutedEventArgs e) =>
            _themePicker.PickAccent((sender as FrameworkElement)?.Tag);
    }
}
