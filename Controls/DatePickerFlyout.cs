using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>Compact themed month picker for the appointment sidebar date fields.</summary>
    internal static class DatePickerFlyout
    {
        internal static void Open(TextBox target, UIElement anchor)
        {
            DateTime selected = DateFormatManager.TryParse(target.Text, out var parsed)
                ? parsed.Date : DateTime.Today;
            var menu = new ContextMenu
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
            };
            Build(menu, target, selected, new DateTime(selected.Year, selected.Month, 1));
            menu.IsOpen = true;
        }

        private static void Build(ContextMenu menu, TextBox target, DateTime selected, DateTime month)
        {
            menu.Items.Clear();
            var root = new StackPanel { Width = 210, Margin = new Thickness(10) };

            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            header.Children.Add(Nav(((char)0xE76B).ToString(), () => Build(menu, target, selected, month.AddMonths(-1))));
            var title = Text(month.ToString("MMMM yyyy", CultureInfo.CurrentCulture), "TextBrush", 12);
            title.FontWeight = FontWeights.SemiBold;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            var next = Nav(((char)0xE76C).ToString(), () => Build(menu, target, selected, month.AddMonths(1)));
            Grid.SetColumn(next, 2);
            header.Children.Add(next);
            root.Children.Add(header);

            var days = new Grid();
            for (int c = 0; c < 7; c++)
                days.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            for (int r = 0; r < 7; r++)
                days.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 0 ? 20 : 26) });

            var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
            for (int c = 0; c < 7; c++)
            {
                var dow = (DayOfWeek)(((int)firstDay + c) % 7);
                var label = Text(names[(int)dow].Substring(0, 1).ToUpperInvariant(), "DimTextBrush", 9.5);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(label, c);
                days.Children.Add(label);
            }

            int shift = ((int)month.DayOfWeek - (int)firstDay + 7) % 7;
            var start = month.AddDays(-shift);
            for (int i = 0; i < 42; i++)
            {
                var date = start.AddDays(i);
                bool chosen = date == selected;
                var number = Text(date.Day.ToString(CultureInfo.CurrentCulture),
                    chosen ? "SelectionFg" : date.Month == month.Month ? "TextBrush" : "DimTextBrush", 10.5);
                number.HorizontalAlignment = HorizontalAlignment.Center;
                number.VerticalAlignment = VerticalAlignment.Center;
                var cell = new Border
                {
                    Child = number,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                };
                if (chosen) cell.SetResourceReference(Border.BackgroundProperty, "SelectionBg");
                else
                {
                    cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
                    cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
                }
                var picked = date;
                cell.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    target.Text = DateFormatManager.Format(picked);
                    target.CaretIndex = target.Text.Length;
                    menu.IsOpen = false;
                    target.Focus();
                };
                Grid.SetRow(cell, i / 7 + 1);
                Grid.SetColumn(cell, i % 7);
                days.Children.Add(cell);
            }
            root.Children.Add(days);

            menu.Items.Add(new MenuItem
            {
                Header = root,
                StaysOpenOnClick = true,
                Style = (Style)Application.Current.FindResource("PanelMenuItem"),
            });
        }

        private static Border Nav(string glyph, Action click)
        {
            var label = Text(glyph, "TextBrush", 10, "Segoe MDL2 Assets");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            var border = new Border { Child = label, CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand };
            border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
            border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
            return border;
        }

        private static TextBlock Text(string value, string brush, double size, string font = "Consolas")
        {
            var text = new TextBlock { Text = value, FontFamily = new FontFamily(font), FontSize = size };
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return text;
        }
    }
}
