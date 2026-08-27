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
            (Locale.HuHU, "Magyar", "hu-HU"),
            (Locale.Ja,   "日本語", "ja-JP"),
            (Locale.PlPL, "Polski", "pl-PL"),
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
            // (2026-07-30)
            FlyoutPlacement.Attach(_menu, _anchor);
            _menu.IsOpen = true;
            Anim.FadeIn(_menu);
        }

        private static Brush? Accent => Application.Current.TryFindResource("PrimaryBrush") as Brush;

        private void Build()
        {
            _menu.Items.Clear();
            var current = LocaleManager.Current;
            // Content-sized and deliberately narrow: the old 190px body plus the flyout's shadow
            // room made this compact picker read like a dialog. The longest native name and locale
            // code still fit at 160px without forcing the menu across the calendar.
            var panel = new StackPanel { Width = 160, Margin = new Thickness(10, 10, 10, 10) };
            foreach (var (loc, name, code) in Languages)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var nameBlock = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
                var codeBlock = new TextBlock
                {
                    Text = code,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                codeBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                Grid.SetColumn(codeBlock, 1);
                grid.Children.Add(nameBlock);
                grid.Children.Add(codeBlock);

                var item = new RadioButton
                {
                    Content = grid,
                    Tag = loc.ToString(),
                    GroupName = "LangGroup",
                    Style = (Style)Application.Current.FindResource("ThemeRadio"),
                    IsChecked = loc == current,
                };
                item.Checked += LocaleItem_Click;
                panel.Children.Add(item);
            }
            panel.Children.Add(new Border
            {
                Height = 1, Margin = new Thickness(0, 2, 0, 8),
                Background = Application.Current.TryFindResource("MenuBorderBrush") as Brush
            });
            panel.Children.Add(new TextBlock
            {
                Text = LocaleManager.Loc("Str_Lbl_DateFormat"), FontSize = 10,
                Margin = new Thickness(0, 0, 0, 6)
            });
            foreach (var (style, key) in DateStyles)
            {
                var date = new RadioButton
                {
                    Content = LocaleManager.Loc(key), Tag = style.ToString(), GroupName = "DateGroup",
                    Style = (Style)Application.Current.FindResource("ThemeRadio"),
                    IsChecked = style == DateFormatManager.Current,
                };
                date.Checked += DateStyleItem_Click;
                panel.Children.Add(date);
            }
            // A raw panel added to ContextMenu is auto-wrapped in the normal MenuItem template,
            // which reserves an icon gutter and row padding around the WHOLE picker. This is a
            // custom menu panel, like the theme swatches, so use the shared gutter-free container.
            _menu.Items.Add(new MenuItem
            {
                Header = panel,
                StaysOpenOnClick = true,
                Style = (Style)Application.Current.FindResource("PanelMenuItem"),
            });
        }

        private void LocaleItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton mi && mi.Tag is string tag && Enum.TryParse<Locale>(tag, out var loc))
            {
                LocaleManager.Apply(loc);
                _localeChanged();
                _menu.IsOpen = false;
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
            if (sender is RadioButton mi && mi.Tag is string tag &&
                Enum.TryParse<DateStyle>(tag, out var style))
            {
                DateFormatManager.Apply(style);
                _dateStyleChanged();
            }
        }
    }
}
