using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
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
        private readonly ContextMenu _menu;
        private readonly UIElement _button;
        private Grid? _accentHost;
        private readonly List<Border> _accentDots = [];
        internal ThemePicker(Window window, ContextMenu menu, UIElement themeButton)
        {
            _window = window;
            _menu = menu;
            _button = themeButton;
            // This is a palette tester, not a one-shot command menu. Both flags matter: StaysOpen
            // keeps the ContextMenu alive, while StaysOpenOnClick prevents its generated MenuItem
            // container from dismissing it after a RadioButton click.
            _menu.StaysOpen = true;
            var baseMenuItem = Application.Current.TryFindResource(typeof(MenuItem)) as Style;
            var keepOpenItems = new Style(typeof(MenuItem), baseMenuItem);
            keepOpenItems.Setters.Add(new Setter(MenuItem.StaysOpenOnClickProperty, true));
            _menu.ItemContainerStyle = keepOpenItems;
        }

        /// <summary>Themes that carry a user-chosen accent. The rest are fixed palettes, so their
        /// accent row is hidden rather than shown doing nothing.</summary>
        private static bool HasAccents(Theme t) =>
            t == Theme.Dark || t == Theme.Light || t == Theme.Black || t == Theme.SE98;

        /// <summary>
        /// Opens or closes the menu. Identical to LanguageMenu.Open - same control type, same
        /// placement call, same fade - because this IS a ContextMenu now. It used to be a Popup
        /// with its own chrome and its own open animation, which is why it never matched the
        /// locale menu no matter how many times the two were tuned by hand.
        /// (2026-07-30)
        /// </summary>
        internal void Toggle()
        {
            if (_menu.IsOpen) { _menu.IsOpen = false; return; }

            BuildPdfMenu();
            FlyoutPlacement.Attach(_menu, _button);
            _menu.IsOpen = true;
            Anim.FadeIn(_menu);
        }

        private void BuildPdfMenu(bool animateAccent = false)
        {
            _menu.Items.Clear();
            _accentHost = null;
            _accentDots.Clear();
            // Measured against the rendered divider/menu edge: trim 3px from the pill's left
            // inset and 6px from the remaining right edge to leave 8px of visible air per side.
            // This is the Killendar trial value before the family copy.
            var panel = new Grid { Margin = new Thickness(12, 10, 3, 10) };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var list = new StackPanel { Width = 120 };
            Grid.SetColumn(list, 0);
            panel.Children.Add(list);

            Theme[] themes =
            [
                Theme.Dark, Theme.Light, Theme.Black, Theme.SE98, Theme.Blood, Theme.Greed,
                Theme.Cyanotic, Theme.Ectoplasm, Theme.Decay, Theme.Malaise, Theme.Sepulchre,
                Theme.Delirium, Theme.Mourning
            ];
            foreach (Theme theme in themes)
            {
                var radio = new RadioButton { Content = DisplayName(theme), Tag = theme, GroupName = "ThemeGroup",
                    Style = (Style)Application.Current.FindResource("ThemeRadio"), IsChecked = ThemeManager.Current == theme };
                radio.Checked += (_, _) =>
                {
                    if (theme == ThemeManager.Current) return;
                    ThemeManager.Apply(theme);
                    DwmChrome.SetRoundedCorners(_window, rounded: theme != Theme.SE98
                                                                 && _window.WindowState == WindowState.Normal);
                    DwmChrome.SetThemeBorder(_window);
                    ReplaceAccentStrip(panel, theme, animate: true);
                };
                list.Children.Add(radio);
            }

            if (HasAccents(ThemeManager.Current))
            {
                _accentHost = BuildAccentStrip(ThemeManager.Current);
                panel.Children.Add(_accentHost);
                if (animateAccent)
                {
                    _accentHost.Width = 0;
                    _accentHost.ClipToBounds = true;
                    var slide = new DoubleAnimation(0, 39, TimeSpan.FromMilliseconds(170))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    slide.Completed += (_, _) =>
                    {
                        if (_accentHost != null)
                        {
                            _accentHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                            _accentHost.Width = 39;
                        }
                    };
                    _accentHost.BeginAnimation(FrameworkElement.WidthProperty, slide);
                }
            }

            _menu.Items.Add(panel);
        }

        private void ReplaceAccentStrip(Grid panel, Theme family, bool animate)
        {
            if (_accentHost != null) panel.Children.Remove(_accentHost);
            _accentHost = null;
            _accentDots.Clear();
            if (!HasAccents(family)) return;

            _accentHost = BuildAccentStrip(family);
            panel.Children.Add(_accentHost);
            if (!animate) return;

            _accentHost.Width = 0;
            _accentHost.ClipToBounds = true;
            var slide = new DoubleAnimation(0, 39, TimeSpan.FromMilliseconds(170))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            slide.Completed += (_, _) =>
            {
                if (_accentHost == null) return;
                _accentHost.BeginAnimation(FrameworkElement.WidthProperty, null);
                _accentHost.Width = 39;
            };
            _accentHost.BeginAnimation(FrameworkElement.WidthProperty, slide);
        }

        /// <summary>KillerPDF's current picker: one vertical strip beside the complete theme list,
        /// repainted for whichever accent-capable family is selected.</summary>
        private Grid BuildAccentStrip(Theme family)
        {
            var host = new Grid { Width = 39 };
            Grid.SetColumn(host, 1);

            host.Children.Add(new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 6),
                Background = Application.Current.TryFindResource("MenuBorderBrush") as Brush,
            });

            var strip = new Grid { Margin = new Thickness(7, 6, 2, 6) };
            var colors = StripColors(family);
            for (int i = 0; i < colors.Length; i++)
            {
                strip.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var (accent, hex) = colors[i];
                var dot = new Border
                {
                    Tag = accent,
                    Style = (Style)Application.Current.FindResource("AccentDot"),
                    Width = 26,
                    Height = double.NaN,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, i == colors.Length - 1 ? 0 : 8),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                };
                if (ThemeManager.AccentChoiceFor(family) == accent)
                    dot.BorderBrush = Application.Current.TryFindResource("TextBrush") as Brush;
                dot.MouseLeftButtonUp += (_, _) =>
                {
                    if (ThemeManager.AccentChoiceFor(family) == accent) return;
                    CrossfadeAccent(() => ThemeManager.ApplyAccent(family, accent),
                                    () => DwmChrome.SetThemeBorder(_window));
                    RingAccentStrip(family);
                };
                _accentDots.Add(dot);
                Grid.SetRow(dot, i);
                strip.Children.Add(dot);
            }
            host.Children.Add(strip);
            return host;
        }

        private void RingAccentStrip(Theme family)
        {
            var selected = ThemeManager.AccentChoiceFor(family);
            Brush? ring = Application.Current.TryFindResource("TextBrush") as Brush;
            foreach (Border dot in _accentDots)
                dot.BorderBrush = dot.Tag is Accent accent && accent == selected ? ring : Brushes.Transparent;
        }

        /// <summary>Hide the synchronous DynamicResource repaint beneath a snapshot and reveal
        /// the new accent as one composed frame. This matters most in 98SE, where title, frame,
        /// selection, calendar bands and controls all change together.</summary>
        private void CrossfadeAccent(Action swap, Action frame)
        {
            if (_window.Content is not Panel root || root.ActualWidth <= 0 || root.ActualHeight <= 0)
            {
                swap();
                frame();
                return;
            }

            Image? ghost = null;
            try
            {
                var bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                bitmap.Freeze();
                ghost = new Image { Source = bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false };
                Panel.SetZIndex(ghost, 10000);
                if (root is Grid grid)
                {
                    Grid.SetRowSpan(ghost, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(ghost, Math.Max(1, grid.ColumnDefinitions.Count));
                }
                root.Children.Add(ghost);
            }
            catch { ghost = null; }

            swap();
            if (ghost == null) { frame(); return; }
            Image held = ghost;
            _window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() =>
                {
                    frame();
                    var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(190))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    fade.Completed += (_, _) => root.Children.Remove(held);
                    held.BeginAnimation(UIElement.OpacityProperty, fade);
                }));
        }

        private static (Accent Accent, string Hex)[] StripColors(Theme family) => family switch
        {
            Theme.Light =>
            [
                (Accent.Red, "#931A1A"), (Accent.Orange, "#C7710F"), (Accent.Green, "#1B5E20"),
                (Accent.Teal, "#0D827E"), (Accent.Blue, "#18608E"), (Accent.Purple, "#5A1690")
            ],
            Theme.Black =>
            [
                (Accent.Red, "#FF2929"), (Accent.Orange, "#FF910A"), (Accent.Green, "#00FF66"),
                (Accent.Teal, "#0AFFE7"), (Accent.Blue, "#298DFF"), (Accent.Purple, "#B829FF")
            ],
            Theme.SE98 =>
            [
                (Accent.Red, "#800040"), (Accent.Orange, "#A05000"), (Accent.Green, "#006000"),
                (Accent.Teal, "#008080"), (Accent.Blue, "#000080"), (Accent.Purple, "#5A376E")
            ],
            _ =>
            [
                (Accent.Red, "#DD504B"), (Accent.Orange, "#E8962C"), (Accent.Green, "#1EA54C"),
                (Accent.Teal, "#1FB8A8"), (Accent.Blue, "#4580D9"), (Accent.Purple, "#B982E3")
            ],
        };

        private static string DisplayName(Theme theme)
        {
            string key = theme switch
            {
                Theme.SE98 => "Str_Theme_98SE",
                _ => "Str_Theme_" + theme,
            };
            return Application.Current.TryFindResource(key) as string ?? theme.ToString();
        }

    }
}
