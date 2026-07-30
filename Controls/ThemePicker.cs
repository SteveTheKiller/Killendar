using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>
    /// The title-bar theme and accent pickers: the flyout, the selection rings, and applying what
    /// was picked. Constructed with the elements it drives, so it looks nothing up by name.
    /// </summary>
    internal sealed class ThemePicker
    {
        private readonly Window _window;
        private readonly Popup _popup;
        private readonly Panel _themeSwatches;
        private readonly Panel _accentSwatches;
        private readonly UIElement _accentLabel;

        internal ThemePicker(Window window, Popup popup, Panel themeSwatches,
                             Panel accentSwatches, UIElement accentLabel)
        {
            _window         = window;
            _popup          = popup;
            _themeSwatches  = themeSwatches;
            _accentSwatches = accentSwatches;
            _accentLabel    = accentLabel;
        }

        /// <summary>Themes that carry a user-chosen accent. The rest are fixed palettes, so their
        /// accent row is hidden rather than shown doing nothing.</summary>
        private static bool HasAccents(Theme t) =>
            t == Theme.Dark || t == Theme.Light || t == Theme.Black;

        /// <summary>Opens or closes the flyout. It slides out of the rail rather than only fading, so
        /// it reads as coming from the button that was pressed; negative dx comes in from the left,
        /// toward the calendar.</summary>
        internal void Toggle()
        {
            _popup.IsOpen = !_popup.IsOpen;
            if (_popup.IsOpen && _popup.Child is UIElement child) Anim.SlideInX(child, -12);
        }

        /// <summary>Applies the theme named on a swatch's Tag.</summary>
        internal void PickTheme(object? tag)
        {
            if (tag is not string name || !Enum.TryParse<Theme>(name, out var theme)) return;
            ThemeManager.Apply(theme);
            DwmChrome.SetThemeBorder(_window);   // retint the frame border to the new palette
            Refresh();
        }

        /// <summary>Applies the accent named on a swatch's Tag.</summary>
        internal void PickAccent(object? tag)
        {
            if (tag is not string name || !Enum.TryParse<Accent>(name, out var accent)) return;
            ThemeManager.ApplyAccent(ThemeManager.Current, accent);
            Refresh();
        }

        /// <summary>Re-seeds both rows. The ring colour comes from the accent, so a change to either
        /// one repaints both.</summary>
        internal void Refresh()
        {
            Highlight(_themeSwatches, ThemeManager.Current.ToString());

            var theme = ThemeManager.Current;
            var vis   = HasAccents(theme) ? Visibility.Visible : Visibility.Collapsed;
            _accentSwatches.Visibility = vis;
            _accentLabel.Visibility    = vis;
            if (HasAccents(theme))
                Highlight(_accentSwatches, ThemeManager.AccentChoiceFor(theme).ToString());
        }

        private void Highlight(Panel panel, string current)
        {
            var activeRing = _window.TryFindResource("PrimaryBrush") as Brush ?? Brushes.White;
            var idleRing   = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            foreach (var child in panel.Children)
            {
                if (child is not Button b || b.Tag is not string name) continue;
                bool active = name == current;
                b.BorderBrush     = active ? activeRing : idleRing;
                b.BorderThickness = new Thickness(active ? 2 : 1);
            }
        }
    }
}
