using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Killendar.Services;

// The Killendar - language picker on the sidebar rail. Partial of MainWindow.
// MainWindow.xaml provides a LangButton whose Button.ContextMenu is x:Name="LangMenu".
namespace Killendar
{
    public partial class MainWindow
    {
        // English pinned first; the rest alphabetical by locale code. Native name left, code right.
        private static readonly (Locale Loc, string Name, string Code)[] Languages =
        {
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
        };

        private void LangButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.ContextMenu != null)
            {
                BuildLanguageMenu(b.ContextMenu);
                // Same fixed anchor the theme flyout uses, so the two rail menus open in the
                // same place rather than one tracking the button and one not.
                b.ContextMenu.PlacementTarget = RailFlyoutAnchor;
                b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                b.ContextMenu.IsOpen = true;
                Anim.FadeIn(b.ContextMenu);
            }
        }

        private void BuildLanguageMenu(ContextMenu menu)
        {
            menu.Items.Clear();
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
                if (loc == current && TryFindResource("PrimaryBrush") is Brush accent)
                {
                    nameBlock.Foreground = accent;
                    nameBlock.FontWeight = FontWeights.SemiBold;
                    codeBlock.Foreground = accent;
                    codeBlock.Opacity = 0.85;
                }
                item.Click += Lang_Click;
                menu.Items.Add(item);
            }

            AppendDateFormatSection(menu);
        }

        // Date format lives in this menu rather than on its own rail button (Steve, 2026-07-29):
        // it is locale-adjacent, and a fourth rail icon for one setting is not worth the width.
        private static readonly (DateStyle Style, string Key)[] DateStyles =
        {
            (DateStyle.FollowWindows, "Str_Date_FollowWindows"),
            (DateStyle.Iso,           "Str_Date_Iso"),
            (DateStyle.US,            "Str_Date_US"),
            (DateStyle.EU,            "Str_Date_EU"),
        };

        private void AppendDateFormatSection(ContextMenu menu)
        {
            menu.Items.Add(new Separator());

            // A disabled item as a section heading: it inherits the menu's themed chrome, so it
            // needs no template of its own.
            menu.Items.Add(new MenuItem
            {
                Header = Loc("Str_Lbl_DateFormat"),
                IsEnabled = false,
                FontSize = 10,
            });

            var current = DateFormatManager.Current;
            foreach (var (style, key) in DateStyles)
            {
                var text = new TextBlock
                {
                    Text = Loc(key),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var item = new MenuItem
                {
                    Header = text,
                    Tag = style.ToString(),
                    IsChecked = style == current,
                };
                if (style == current && TryFindResource("PrimaryBrush") is Brush accent)
                {
                    text.Foreground = accent;
                    text.FontWeight = FontWeights.SemiBold;
                }
                item.Click += DateStyle_Click;
                menu.Items.Add(item);
            }
        }

        private void DateStyle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag && Enum.TryParse<DateStyle>(tag, out var style))
            {
                DateFormatManager.Apply(style);

                // An open editor is showing dates in the old pattern, so reformat what is in the
                // fields rather than leaving a mix of the two.
                if (_sidebarOpen)
                {
                    if (TryParseDate(FieldStartDate.Text, out var s)) FieldStartDate.Text = DateFormatManager.Format(s);
                    if (TryParseDate(FieldEndDate.Text, out var en)) FieldEndDate.Text = DateFormatManager.Format(en);
                    RefreshDateHints();
                }
            }
        }

        private void Lang_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag && Enum.TryParse<Locale>(tag, out var loc))
            {
                LocaleManager.Apply(loc);
                RelocalizeDynamicUi();
            }
        }

        /// <summary>Look up a localized string; falls back to the key name if missing.</summary>
        private string Loc(string key) => LocStatic(key);

        /// <summary>The same lookup without a window, for code that has no `this`.</summary>
        internal static string LocStatic(string key)
            => Application.Current.TryFindResource(key) as string ?? key;

        /// <summary>
        /// Re-applies strings that were set from code, so a live language switch updates them.
        /// Static {DynamicResource Str_*} in XAML refreshes itself; this is the remainder -
        /// anything a handler assigned to .Text or .Content, plus the code-built calendar views.
        /// </summary>
        private void RelocalizeDynamicUi()
        {
            // The views are rebuilt wholesale, which re-reads every string they use.
            _active.Refresh();
            UpdatePeriodLabel();

            // Rail tooltips track the panel state.
            SidebarToggleBtn.ToolTip = Loc(_sidebarOpen ? "Str_TT_PanelHide" : "Str_TT_PanelShow");

            // The sidebar's own dynamic bits: heading, all-day toggle, and any visible error.
            SidebarTitle.Text = Loc(_editing == null ? "Str_Side_New" : "Str_Side_Edit");
            ApplyAllDayState();

            // The status line is transient by nature; put it back to a neutral, translated idle
            // rather than leaving the previous language's sentence sitting there.
            StatusText.Text = Loc("Str_Status_Ready");
        }
    }
}
