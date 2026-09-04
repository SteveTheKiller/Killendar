using System;
using System.Windows;
using System.Windows.Controls;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>The rail language picker, rebuilt on open to reflect the current locale.</summary>
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
            (Locale.It,   "Italiano", "it-IT"),
            (Locale.Ja,   "日本語", "ja-JP"),
            (Locale.KkKZ, "Қазақша", "kk-KZ"),
            (Locale.PlPL, "Polski", "pl-PL"),
            (Locale.RuRU, "Русский", "ru-RU"),
            (Locale.TrTR, "Türkçe", "tr-TR"),
            (Locale.ZhCN, "中文 (简体)", "zh-CN"),
            (Locale.ZhTW, "中文 (繁體)", "zh-TW"),
        ];

        private readonly ContextMenu _menu;
        private readonly UIElement _anchor;
        private readonly Action _localeChanged;

        internal LanguageMenu(ContextMenu menu, UIElement anchor,
                              Action localeChanged)
        {
            _menu             = menu;
            _anchor           = anchor;
            _localeChanged    = localeChanged;
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

        private void Build()
        {
            _menu.Items.Clear();
            var current = LocaleManager.Current;

            // Two columns. Fifteen languages in one stack ran nearly the full window height and
            // clipped on a short window, and a flyout that cannot show its last row is broken
            // whatever it looks like. Split down the middle rather than balanced by height, so
            // the left column stays in reading order and English stays at the top of it.
            var columns = new StackPanel { Orientation = Orientation.Horizontal,
                                           Margin = new Thickness(10, 10, 10, 10) };
            // Content-sized and deliberately narrow per column: the longest native name and its
            // locale code still fit at 160px.
            var left = new StackPanel { Width = 160 };
            var right = new StackPanel { Width = 160, Margin = new Thickness(14, 0, 0, 0) };
            columns.Children.Add(left);
            columns.Children.Add(right);
            int half = (Languages.Length + 1) / 2;
            int index = 0;

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
                (index++ < half ? left : right).Children.Add(item);
            }
            // A raw panel added to ContextMenu is auto-wrapped in the normal MenuItem template,
            // which reserves an icon gutter and row padding around the WHOLE picker. This is a
            // custom menu panel, like the theme swatches, so use the shared gutter-free container.
            _menu.Items.Add(new MenuItem
            {
                Header = columns,
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
    }
}
