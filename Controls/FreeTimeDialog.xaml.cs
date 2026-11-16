using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>Lists open time in the shown calendars. A click on a gap closes with Chosen set to
    /// its start and Length to the searched length; the shell opens the new appointment.</summary>
    public partial class FreeTimeDialog : Window
    {
        internal DateTime? Chosen { get; private set; }
        internal TimeSpan Length => _length;

        private readonly Func<DateTime, DateTime, List<CalendarEvent>> _events;
        private TimeSpan _length = TimeSpan.FromHours(1);
        private int _days = 7;

        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        internal FreeTimeDialog(Func<DateTime, DateTime, List<CalendarEvent>> events)
        {
            _events = events;
            InitializeComponent();
            TitleIcon.Source = BitmapDecoder.Create(new Uri("pack://application:,,,/Resources/kd-icon.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            TaskbarIdentity.Track(this);

            foreach (var (minutes, key) in new[] { (30, "Str_Free_30m"), (60, "Str_Free_1h"), (120, "Str_Free_2h"), (180, "Str_Free_3h") })
                LengthRow.Children.Add(Chip("FreeLength", Loc(key), minutes == 60, () => { _length = TimeSpan.FromMinutes(minutes); Refresh(); }));
            foreach (var (days, key) in new[] { (1, "Str_Free_Today"), (7, "Str_Free_7d"), (14, "Str_Free_14d"), (30, "Str_Free_30d") })
                RangeRow.Children.Add(Chip("FreeRange", Loc(key), days == 7, () => { _days = days; Refresh(); }));

            Loaded += (_, _) =>
            {
                Anim.FadeIn(RootBorder);
                Results.Focus();
            };
            Refresh();
        }

        private static RadioButton Chip(string group, string text, bool on, Action picked)
        {
            var chip = new RadioButton { Content = text, GroupName = group, IsChecked = on, Margin = new Thickness(0, 0, 14, 0) };
            chip.SetResourceReference(StyleProperty, "ThemeRadio");
            chip.Checked += (_, _) => picked();
            return chip;
        }

        private void Option_Changed(object sender, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            if (Results == null || EmptyText == null || WorkHoursBox == null) return;
            Results.Items.Clear();
            var now = DateTime.Now;
            var to = now.Date.AddDays(_days);
            foreach (var gap in FreeTime.Find(_events(now.Date, to), now, to, _length, WorkHoursBox.IsChecked == true))
            {
                string day = DateFormatManager.Format(gap.Start);
                string span = DateFormatManager.Time(gap.Start) + " - " +
                              (gap.End.Date == gap.Start.Date ? DateFormatManager.Time(gap.End)
                                                              : DateFormatManager.Format(gap.End) + " " + DateFormatManager.Time(gap.End));
                Results.Items.Add(new ListBoxItem { Content = Row(gap.Start.ToString("dddd") + "  " + day, span), Tag = gap.Start });
            }
            if (Results.Items.Count > 0) Results.SelectedIndex = 0;
            bool none = Results.Items.Count == 0;
            Results.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            EmptyText.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        }

        private static FrameworkElement Row(string main, string detail)
        {
            var row = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
            var heading = new TextBlock { Text = main, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            row.Children.Add(heading);
            var line = new TextBlock { Text = detail, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
            line.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            row.Children.Add(line);
            return row;
        }

        private void Choose()
        {
            if ((Results.SelectedItem as ListBoxItem)?.Tag is not DateTime start) return;
            Chosen = start;
            Close();
        }

        // One click on a gap picks it, as the plan says; the scrollbar and empty space do not.
        private void Results_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (SearchDialog.IsOnRow(e.OriginalSource as DependencyObject)) Choose();
        }

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SearchDialog.IsOnRow(e.OriginalSource as DependencyObject)) Choose();
        }

        private void Results_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            base.OnPreviewKeyDown(e);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            TitleBarDrag.Start(this, e);
    }
}
