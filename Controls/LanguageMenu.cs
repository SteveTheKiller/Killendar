using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>
    /// The rail's language menu, which also carries the date-format choice: the format is
    /// locale-adjacent, and a whole rail icon for one setting is not worth the width.
    ///
    /// Items are rebuilt on every open, so the check marks and the accent highlight always reflect
    /// the current locale, date style and accent.
    /// </summary>
    internal sealed class LanguageMenu
    {
        // English pinned first; the rest alphabetical by locale code. Native name left, code right.
        private static readonly (Locale Loc, string Name, string Code)[] Languages =
        [
            (Locale.EnUS, "English",    "en-US"),
            (Locale.Bn,   "বাংলা", "bn"),
            (Locale.Cs,   "Čeština", "cs-CZ"),
            (Locale.De,   "Deutsch",    "de-DE"),
            (Locale.Es,   "Español", "es"),
            (Locale.Fr,   "Français", "fr-FR"),
            (Locale.Ja,   "日本語", "ja-JP"),
            (Locale.TrTR, "Türkçe", "tr-TR"),
            (Locale.ZhCN, "中文 (简体)", "zh-CN"),
            (Locale.ZhTW, "中文 (繁體)", "zh-TW"),
        ];

        private static readonly (DateStyle Style, string Key)[] DateStyles =
        [
            (DateStyle.FollowWindows, "Str_Date_FollowWindows"),
            (DateStyle.Iso,           "Str_Date_Iso"),
            (DateStyle.US,            "Str_Date_US"),
            (DateStyle.EU,            "Str_Date_EU"),
        ];

        private readonly ContextMenu _menu;
        private readonly UIElement _anchor;
        private readonly Action _localeChanged;
        private readonly Action _dateStyleChanged;

        internal LanguageMenu(ContextMenu menu, UIElement anchor,
                              Action localeChanged, Action dateStyleChanged)
        {
            _menu             = menu;
            _anchor           = anchor;
            _localeChanged    = localeChanged;
            _dateStyleChanged = dateStyleChanged;
        }

        /// <summary>Rebuilds the items and opens the menu.</summary>
        internal void Open()
        {
            Build();
            // Beside the button that opens it and clamped inside the window - FlyoutPlacement.cs
            // does both. PlacementMode.Right alone is not enough: WPF only avoids the SCREEN edge,
            // so with the rail near the window's right side the menu opened over the desktop.
            // (Steve, 2026-07-30.)
            FlyoutPlacement.Attach(_menu, _anchor);
            _menu.IsOpen = true;
            Anim.FadeIn(_menu);
        }

        private static Brush? Accent => Application.Current.TryFindResource("PrimaryBrush") as Brush;

        private void Build()
        {
            _menu.Items.Clear();
            var current = LocaleManager.Current;

            foreach (var (loc, name, code) in Languages)
            {
                var grid = new Grid { MinWidth = 160 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBlock = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
                var codeBlock = new TextBlock
                {
                    Text = "(" + code + ")",
                    Opacity = 0.5,
                    Margin = new Thickness(22, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(codeBlock, 1);
                grid.Children.Add(nameBlock);
                grid.Children.Add(codeBlock);

                var item = new MenuItem
                {
                    Header = grid,
                    Tag = loc.ToString(),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsChecked = loc == current,
                };
                if (loc == current && Accent is Brush accent)
                {
                    nameBlock.Foreground = accent;
                    nameBlock.FontWeight = FontWeights.SemiBold;
                    codeBlock.Foreground = accent;
                    codeBlock.Opacity    = 0.85;
                }
                item.Click += LocaleItem_Click;
                _menu.Items.Add(item);
            }

            AppendDateFormatSection();
        }

        private void LocaleItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag && Enum.TryParse<Locale>(tag, out var loc))
            {
                LocaleManager.Apply(loc);
                _localeChanged();
            }
        }

        private void AppendDateFormatSection()
        {
            _menu.Items.Add(new Separator());

            // A disabled item as a section heading: it inherits the menu's themed chrome, so it
            // needs no template of its own.
            _menu.Items.Add(new MenuItem
            {
                Header    = LocaleManager.Loc("Str_Lbl_DateFormat"),
                IsEnabled = false,
                FontSize  = 10,
            });

            var current = DateFormatManager.Current;
            foreach (var (style, key) in DateStyles)
            {
                var text = new TextBlock
                {
                    Text = LocaleManager.Loc(key),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var item = new MenuItem
                {
                    Header = text,
                    Tag = style.ToString(),
                    IsChecked = style == current,
                };
                if (style == current && Accent is Brush accent)
                {
                    text.Foreground = accent;
                    text.FontWeight = FontWeights.SemiBold;
                }
                item.Click += DateStyleItem_Click;
                _menu.Items.Add(item);
            }
        }

        private void DateStyleItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag &&
                Enum.TryParse<DateStyle>(tag, out var style))
            {
                DateFormatManager.Apply(style);
                _dateStyleChanged();
            }
        }
    }
}
