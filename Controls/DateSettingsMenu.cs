using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>Date format, first day of the week, agenda length and default reminder choices for the rail.</summary>
    internal sealed class DateSettingsMenu
    {
        private static readonly (DateStyle Style, string Key)[] DateStyles =
        [
            (DateStyle.FollowWindows, "Str_Date_FollowWindows"),
            (DateStyle.Iso,           "Str_Date_Iso"),
            (DateStyle.US,            "Str_Date_US"),
            (DateStyle.EU,            "Str_Date_EU"),
        ];

        private static readonly (TimeStyle Style, string Key)[] TimeStyles =
        [
            (TimeStyle.FollowWindows,  "Str_Date_FollowWindows"),
            (TimeStyle.TwelveHour,     "Str_Time_12"),
            (TimeStyle.TwentyFourHour, "Str_Time_24"),
        ];

        private readonly ContextMenu _menu;
        private readonly UIElement _anchor;
        private readonly Action<DateStyle> _dateStyleChanged;
        private readonly Action<TimeStyle> _timeStyleChanged;
        private readonly Action _weekStartChanged;

        internal DateSettingsMenu(ContextMenu menu, UIElement anchor,
                                  Action<DateStyle> dateStyleChanged, Action<TimeStyle> timeStyleChanged,
                                  Action weekStartChanged)
        {
            _menu = menu;
            _anchor = anchor;
            _dateStyleChanged = dateStyleChanged;
            _timeStyleChanged = timeStyleChanged;
            _weekStartChanged = weekStartChanged;
            if (_anchor is FrameworkElement button)
                button.ContextMenuOpening += (_, e) => { e.Handled = true; Open(); };
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
            // Columns side by side - date and clock, agenda length, work week, default reminder - so the
            // flyout stays about as tall as one group instead of running down the screen.
            var columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
            var panel = new StackPanel { MinWidth = 150 };
            columns.Children.Add(panel);
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
            AddSeparator(panel);
            panel.Children.Add(new TextBlock
            {
                Text = LocaleManager.Loc("Str_Lbl_TimeFormat"), FontSize = 10,
                Margin = new Thickness(0, 0, 0, 6)
            });
            foreach (var (style, key) in TimeStyles)
            {
                var time = new RadioButton
                {
                    Content = LocaleManager.Loc(key), Tag = style.ToString(), GroupName = "TimeGroup",
                    Style = (Style)Application.Current.FindResource("ThemeRadio"),
                    IsChecked = style == DateFormatManager.CurrentTime,
                };
                time.Checked += TimeStyleItem_Click;
                panel.Children.Add(time);
            }
            AddSeparator(panel);
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
            panel = NewColumn(columns);
            panel.Children.Add(new TextBlock
            {
                Text = LocaleManager.Loc("Str_Lbl_AgendaDays"), FontSize = 10,
                Margin = new Thickness(0, 0, 0, 6)
            });
            int currentDays = Views.AgendaView.HorizonDays(Settings.Get(Views.AgendaView.HorizonSetting));
            foreach (int days in Views.AgendaView.HorizonChoices)
            {
                var agenda = new RadioButton
                {
                    Content = string.Format(LocaleManager.Loc("Str_Agenda_Days"), days), GroupName = "AgendaGroup",
                    Style = (Style)Application.Current.FindResource("ThemeRadio"),
                    IsChecked = days == currentDays,
                };
                agenda.Checked += (_, _) =>
                {
                    Settings.Set(Views.AgendaView.HorizonSetting, days.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _weekStartChanged();   // repaints the views, the agenda's length with them
                };
                panel.Children.Add(agenda);
            }
            panel = NewColumn(columns);
            BuildWorkWeek(panel);
            panel = NewColumn(columns);
            panel.Children.Add(new TextBlock
            {
                Text = LocaleManager.Loc("Str_Lbl_DefaultReminder"), FontSize = 10,
                Margin = new Thickness(0, 0, 0, 6)
            });
            int? currentReminder = Reminders.ParseDefault(Settings.Get(Reminders.DefaultSetting));
            foreach (int? minutes in new int?[] { null }.Concat(Reminders.Choices.Select(m => (int?)m)))
            {
                var reminder = new RadioButton
                {
                    Content = Reminders.Label(minutes, false, LocaleManager.Loc), GroupName = "ReminderGroup",
                    Style = (Style)Application.Current.FindResource("ThemeRadio"),
                    IsChecked = minutes == currentReminder,
                };
                reminder.Checked += (_, _) => Settings.Set(Reminders.DefaultSetting,
                    minutes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
                panel.Children.Add(reminder);
            }
            _menu.Items.Add(new MenuItem
            {
                Header = columns,
                StaysOpenOnClick = true,
                Style = (Style)Application.Current.FindResource("PanelMenuItem"),
            });
        }

        /// <summary>Workdays as ticks in week order, then the working hours as two hour lists.</summary>
        private void BuildWorkWeek(StackPanel panel)
        {
            panel.Children.Add(new TextBlock
            {
                Text = LocaleManager.Loc("Str_Lbl_WorkDays"), FontSize = 10,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var culture = LocaleManager.CultureFor(LocaleManager.Current);
            for (int i = 0; i < 7; i++)
            {
                var day = (DayOfWeek)(((int)WeekStartManager.FirstDay + i) % 7);
                var tick = new CheckBox
                {
                    Content = culture.DateTimeFormat.GetDayName(day),
                    IsChecked = WorkSchedule.IsWorkday(day),
                    Margin = new Thickness(0, 0, 0, 2),
                };
                tick.Click += (_, _) =>
                {
                    var days = WorkSchedule.Days.ToHashSet();
                    if (tick.IsChecked == true) days.Add(day); else days.Remove(day);
                    if (days.Count == 0) { tick.IsChecked = true; return; }   // a week needs a workday
                    SaveWork(WorkSchedule.FormatDays(days), null);
                };
                panel.Children.Add(tick);
            }
            AddSeparator(panel);
            panel.Children.Add(new TextBlock
            {
                Text = LocaleManager.Loc("Str_Lbl_WorkHours"), FontSize = 10,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var hours = new StackPanel { Orientation = Orientation.Horizontal };
            var from = HourList(0, 23, WorkSchedule.StartHour);
            var to = HourList(1, 24, WorkSchedule.EndHour);
            void Changed()
            {
                if (from.SelectedItem is ComboBoxItem f && to.SelectedItem is ComboBoxItem t &&
                    (int)f.Tag < (int)t.Tag)
                    SaveWork(null, WorkSchedule.FormatHours((int)f.Tag, (int)t.Tag));
            }
            from.SelectionChanged += (_, _) => Changed();
            to.SelectionChanged += (_, _) => Changed();
            hours.Children.Add(from);
            hours.Children.Add(new TextBlock
            {
                Text = LocaleManager.Loc("Str_Work_To"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            });
            hours.Children.Add(to);
            panel.Children.Add(hours);
        }

        private static ComboBox HourList(int first, int last, int selected)
        {
            var list = new ComboBox { MinWidth = 70 };
            for (int h = first; h <= last; h++)
            {
                var item = new ComboBoxItem { Content = DateFormatManager.Time(DateTime.Today.AddHours(h)), Tag = h };
                list.Items.Add(item);
                if (h == selected) list.SelectedItem = item;
            }
            return list;
        }

        private void SaveWork(string? days, string? hours)
        {
            if (days != null) Settings.Set(WorkSchedule.DaysSetting, days);
            if (hours != null) Settings.Set(WorkSchedule.HoursSetting, hours);
            WorkSchedule.Load(Settings.Get(WorkSchedule.DaysSetting), Settings.Get(WorkSchedule.HoursSetting));
            _weekStartChanged();   // repaints the views with the new shading and work week
        }

        /// <summary>A divider, then a fresh column to the right of the ones before it.</summary>
        private static StackPanel NewColumn(StackPanel columns)
        {
            columns.Children.Add(new Border
            {
                Width = 1, Margin = new Thickness(12, 0, 12, 0),
                Background = Application.Current.TryFindResource("MenuBorderBrush") as Brush
            });
            var column = new StackPanel { MinWidth = 120 };
            columns.Children.Add(column);
            return column;
        }

        private static void AddSeparator(Panel panel) => panel.Children.Add(new Border
        {
            Height = 1, Margin = new Thickness(0, 8, 0, 8),
            Background = Application.Current.TryFindResource("MenuBorderBrush") as Brush
        });

        private void TimeStyleItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton item && item.Tag is string tag &&
                Enum.TryParse<TimeStyle>(tag, out var style))
            {
                _timeStyleChanged(style);
            }
        }

        private void DateStyleItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton mi && mi.Tag is string tag &&
                Enum.TryParse<DateStyle>(tag, out var style))
            {
                _dateStyleChanged(style);
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
