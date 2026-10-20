using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Killendar.Services;
using KillerFamily.Accessibility;

namespace Killendar.Controls
{
    /// <summary>Compact themed month picker for the appointment sidebar date fields.</summary>
    internal static class DatePickerFlyout
    {
        internal static void Open(TextBox target, UIElement anchor)
        {
            var menu = Create(target, anchor);
            menu.IsOpen = true;
        }

        internal static ContextMenu Create(TextBox target, UIElement anchor)
        {
            DateTime selected = DateFormatManager.TryParse(target.Text, out var parsed)
                ? parsed.Date : DateTime.Today;
            var menu = new ContextMenu
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
            };
            menu.Opened += (_, _) => menu.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(() => FocusDay(menu, selected)));
            menu.Closed += (_, _) => target.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(() => target.Focus()));
            Build(menu, target, selected, new DateTime(selected.Year, selected.Month, 1));
            return menu;
        }

        private static void Build(ContextMenu menu, TextBox target, DateTime selected, DateTime month, DateTime? focusDate = null)
        {
            menu.Items.Clear();
            var root = new StackPanel { Width = 210, Margin = new Thickness(10) };
            KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
            root.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close(menu, target);
            };

            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            bool canPrevious = month.Year > 1 || month.Month > 1;
            bool canNext = month.Year < 9999 || month.Month < 12;
            header.Children.Add(Nav(((char)0xE76B).ToString(),
                () =>
                {
                    if (!canPrevious) return;
                    var previousMonth = month.AddMonths(-1);
                    Build(menu, target, selected, previousMonth, previousMonth);
                }, canPrevious, target.TryFindResource("Str_TT_Prev") as string ?? "Str_TT_Prev"));
            var title = Text(month.ToString("MMMM yyyy", CultureInfo.CurrentCulture), "TextBrush", 12);
            title.FontWeight = FontWeights.SemiBold;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            var next = Nav(((char)0xE76C).ToString(),
                () =>
                {
                    if (!canNext) return;
                    var nextMonth = month.AddMonths(1);
                    Build(menu, target, selected, nextMonth, nextMonth);
                }, canNext, target.TryFindResource("Str_TT_Next") as string ?? "Str_TT_Next");
            Grid.SetColumn(next, 2);
            header.Children.Add(next);
            root.Children.Add(header);

            var days = new Grid();
            for (int c = 0; c < 7; c++)
                days.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            for (int r = 0; r < 7; r++)
                days.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 0 ? 20 : 26) });

            var firstDay = WeekStartManager.FirstDay;
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
            long firstDayNumber = month.Ticks / TimeSpan.TicksPerDay - shift;
            long lastDayNumber = DateTime.MaxValue.Ticks / TimeSpan.TicksPerDay;
            for (int i = 0; i < 42; i++)
            {
                long dayNumber = firstDayNumber + i;
                if (dayNumber < 0 || dayNumber > lastDayNumber) continue;
                var date = new DateTime(dayNumber * TimeSpan.TicksPerDay);
                bool chosen = date == selected;
                var number = Text(date.Day.ToString(CultureInfo.CurrentCulture),
                    chosen ? "SelectionFg" : date.Month == month.Month ? "TextBrush" : "DimTextBrush", 10.5);
                number.HorizontalAlignment = HorizontalAlignment.Center;
                number.VerticalAlignment = VerticalAlignment.Center;
                var cell = new ToggleButton
                {
                    Content = number,
                    IsChecked = chosen,
                    Tag = date,
                    Margin = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                };
                PrepareButton(cell);
                AutomationProperties.SetName(cell, date.ToString("D", CultureInfo.CurrentCulture));
                if (chosen) cell.SetResourceReference(Control.BackgroundProperty, "SelectionBg");
                else
                {
                    cell.MouseEnter += (_, _) => cell.SetResourceReference(Control.BackgroundProperty, "RowHoverBrush");
                    cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
                }
                var picked = date;
                cell.Checked += (_, _) => Pick(menu, target, picked);
                cell.Unchecked += (_, _) => Pick(menu, target, picked);
                cell.PreviewKeyDown += (_, e) => HandleDateKey(menu, target, selected, month, picked, e);
                Grid.SetRow(cell, i / 7 + 1);
                Grid.SetColumn(cell, i % 7);
                days.Children.Add(cell);
            }
            root.Children.Add(days);

            menu.Items.Add(new MenuItem
            {
                Header = root,
                StaysOpenOnClick = true,
                Focusable = false,
                IsTabStop = false,
                Style = (Style)target.FindResource("PanelMenuItem"),
            });
            if (focusDate.HasValue && menu.IsOpen)
                menu.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                    new Action(() => FocusDay(menu, focusDate.Value)));
        }

        private static void HandleDateKey(ContextMenu menu, TextBox target, DateTime selected,
                                          DateTime month, DateTime date, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                e.Handled = true;
                Pick(menu, target, date);
                return;
            }

            DateTime next;
            if (e.Key == Key.PageUp || e.Key == Key.PageDown)
            {
                e.Handled = true;
                int direction = e.Key == Key.PageUp ? -1 : 1;
                if ((direction < 0 && date.Year == 1 && date.Month == 1)
                    || (direction > 0 && date.Year == 9999 && date.Month == 12)) return;
                next = date.AddMonths(direction);
            }
            else
            {
                int weekday = ((int)date.DayOfWeek - (int)WeekStartManager.FirstDay + 7) % 7;
                int offset;
                switch (e.Key)
                {
                    case Key.Left: offset = -1; break;
                    case Key.Right: offset = 1; break;
                    case Key.Up: offset = -7; break;
                    case Key.Down: offset = 7; break;
                    case Key.Home: offset = -weekday; break;
                    case Key.End: offset = 6 - weekday; break;
                    default: return;
                }
                e.Handled = true;
                long day = date.Ticks / TimeSpan.TicksPerDay + offset;
                if (day < 0 || day > DateTime.MaxValue.Ticks / TimeSpan.TicksPerDay) return;
                next = new DateTime(day * TimeSpan.TicksPerDay);
            }

            if (next.Year != month.Year || next.Month != month.Month)
                Build(menu, target, selected, new DateTime(next.Year, next.Month, 1), next);
            else FocusDay(menu, next);
        }

        private static void FocusDay(ContextMenu menu, DateTime date)
        {
            var root = (StackPanel)((MenuItem)menu.Items[0]).Header;
            var grid = (Grid)root.Children[1];
            foreach (UIElement child in grid.Children)
                if (child is ToggleButton button && Equals(button.Tag, date))
                {
                    FocusManager.SetFocusedElement(menu, button);
                    button.Focus();
                    return;
                }
        }

        private static void Pick(ContextMenu menu, TextBox target, DateTime date)
        {
            target.Text = DateFormatManager.Format(date);
            target.CaretIndex = target.Text.Length;
            Close(menu, target);
        }

        private static void Close(ContextMenu menu, TextBox target)
        {
            menu.IsOpen = false;
            target.Focus();
        }

        private static Button Nav(string glyph, Action click, bool enabled, string name)
        {
            var label = Text(glyph, "TextBrush", 10, "Segoe MDL2 Assets");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            var button = new Button { Content = label, Cursor = Cursors.Hand, IsEnabled = enabled, Background = Brushes.Transparent };
            PrepareButton(button);
            AutomationProperties.SetName(button, name);
            button.MouseEnter += (_, _) => button.SetResourceReference(Control.BackgroundProperty, "RowHoverBrush");
            button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
            button.Click += (_, e) => { e.Handled = true; if (button.IsEnabled) click(); };
            return button;
        }

        private static void PrepareButton(ButtonBase button)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            border.AppendChild(content);
            button.Style = new Style(button.GetType());
            button.Template = new ControlTemplate(button.GetType()) { VisualTree = border };
            button.Padding = new Thickness(0);
            button.BorderThickness = new Thickness(0);
            button.FocusVisualStyle = AccessibleControls.FocusStyleFor(button);
        }

        private static TextBlock Text(string value, string brush, double size, string font = "Consolas")
        {
            var text = new TextBlock { Text = value, FontFamily = new FontFamily(font), FontSize = size };
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return text;
        }
    }
}
