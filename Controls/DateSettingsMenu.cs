using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>Date format and first-day-of-week choices for the rail.</summary>
    internal sealed class DateSettingsMenu
    {
        private static readonly (DateStyle Style, string Key)[] DateStyles =
        [
            (DateStyle.FollowWindows, "Str_Date_FollowWindows"),
            (DateStyle.Iso,           "Str_Date_Iso"),
            (DateStyle.US,            "Str_Date_US"),
            (DateStyle.EU,            "Str_Date_EU"),
        ];

        private readonly ContextMenu _menu;
        private readonly UIElement _anchor;
        private readonly Action _dateStyleChanged;
        private readonly Action _weekStartChanged;

        internal DateSettingsMenu(ContextMenu menu, UIElement anchor,
                                  Action dateStyleChanged, Action weekStartChanged)
        {
            _menu = menu;
            _anchor = anchor;
            _dateStyleChanged = dateStyleChanged;
            _weekStartChanged = weekStartChanged;
        }

        internal void Open()
        {
            Build();
            FlyoutPlacement.Attach(_menu, _anchor);
            _menu.IsOpen = true;
            Anim.FadeIn(_menu);
        }

        private void Build()
        {
            _menu.Items.Clear();
            var panel = new StackPanel { MinWidth = 160, Margin = new Thickness(10) };
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
            panel.Children.Add(new Border
            {
                Height = 1, Margin = new Thickness(0, 8, 0, 8),
                Background = Application.Current.TryFindResource("MenuBorderBrush") as Brush
            });
            panel.Children.Add(new TextBlock
            {
                Text = LocaleManager.Loc("Str_Lbl_WeekStart"), FontSize = 10,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var culture = LocaleManager.CultureFor(LocaleManager.Current);
            foreach (var (style, caption) in new[]
                     {
                         (WeekStartStyle.FollowWindows, LocaleManager.Loc("Str_Date_FollowWindows")),
                         (WeekStartStyle.Sunday, culture.DateTimeFormat.GetDayName(DayOfWeek.Sunday)),
                         (WeekStartStyle.Monday, culture.DateTimeFormat.GetDayName(DayOfWeek.Monday)),
                     })
            {
                var week = new RadioButton
                {
                    Content = caption, Tag = style.ToString(), GroupName = "WeekStartGroup",
                    Style = (Style)Application.Current.FindResource("ThemeRadio"),
                    IsChecked = style == WeekStartManager.Current,
                };
                week.Checked += WeekStartItem_Click;
                panel.Children.Add(week);
            }
            _menu.Items.Add(new MenuItem
            {
                Header = panel,
                StaysOpenOnClick = true,
                Style = (Style)Application.Current.FindResource("PanelMenuItem"),
            });
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

        private void WeekStartItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton item && item.Tag is string tag &&
                Enum.TryParse<WeekStartStyle>(tag, out var style))
            {
                WeekStartManager.Apply(style);
                _weekStartChanged();
            }
        }
    }
}
