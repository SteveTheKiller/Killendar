using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>Finds appointments as you type, or a date to go to. Closes with Chosen or GoTo
    /// set; the shell does the navigating.</summary>
    public partial class SearchDialog : Window
    {
        public CalendarEvent? Chosen { get; private set; }
        public DateTime? GoTo { get; private set; }

        private readonly EventStore _store;
        private SearchSpan _span = SearchSpan.Any;

        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        public SearchDialog(EventStore store)
        {
            _store = store;
            InitializeComponent();
            TitleIcon.Source = BitmapDecoder.Create(new Uri("pack://application:,,,/Resources/kd-icon.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            TaskbarIdentity.Track(this);
            foreach (var (span, key) in new[] { (SearchSpan.Any, "Str_Search_Any"), (SearchSpan.Upcoming, "Str_Search_Upcoming"), (SearchSpan.Past, "Str_Search_Past") })
            {
                var chip = new RadioButton
                {
                    Content = Loc(key), GroupName = "SearchSpan", IsChecked = span == _span,
                    Margin = new Thickness(0, 0, 14, 0),
                };
                chip.SetResourceReference(StyleProperty, "ThemeRadio");
                chip.Checked += (_, _) => { _span = span; Refresh(); };
                SpanRow.Children.Add(chip);
            }
            Loaded += (_, _) =>
            {
                Anim.FadeIn(RootBorder);
                QueryBox.Focus();
            };
            Refresh();
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void Refresh()
        {
            if (Results == null || EmptyText == null) return;   // TextChanged can fire during InitializeComponent
            Results.Items.Clear();
            string text = QueryBox.Text.Trim();
            if (text.Length > 0 && DateFormatManager.TryParse(text, out var date))
                Results.Items.Add(new ListBoxItem
                {
                    Content = Row(string.Format(Loc("Str_Search_GoTo"), date.ToString("D")), ""),
                    Tag = date.Date,
                });
            foreach (var ev in _store.Search(text, _span, DateTime.Now))
            {
                string when = ev.AllDay
                    ? DateFormatManager.Format(ev.Start)
                    : DateFormatManager.Format(ev.Start) + " " + DateFormatManager.Time(ev.Start);
                if (!string.IsNullOrWhiteSpace(ev.Location)) when += "    " + ev.Location;
                string title = string.IsNullOrWhiteSpace(ev.Title) ? Loc("Str_Cal_NoTitle") : ev.Title;
                Results.Items.Add(new ListBoxItem { Content = Row(title, when), Tag = ev });
            }
            if (Results.Items.Count > 0) Results.SelectedIndex = 0;
            bool none = Results.Items.Count == 0;
            Results.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            EmptyText.Text = Loc("Str_Search_None");
            EmptyText.Visibility = none && text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static FrameworkElement Row(string main, string detail)
        {
            var row = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
            row.Children.Add(new TextBlock { Text = main, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
            if (detail.Length > 0)
            {
                var line = new TextBlock { Text = detail, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
                line.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                row.Children.Add(line);
            }
            return row;
        }

        private void Choose()
        {
            switch ((Results.SelectedItem as ListBoxItem)?.Tag)
            {
                case DateTime date: GoTo = date; break;
                case CalendarEvent ev: Chosen = ev; break;
                default: return;
            }
            Close();
        }

        private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter: Choose(); e.Handled = true; break;
                case Key.Escape: Close(); e.Handled = true; break;
                case Key.Down when Results.Items.Count > 0:
                    (Results.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, Results.SelectedIndex)) as ListBoxItem)?.Focus();
                    e.Handled = true;
                    break;
            }
        }

        private void Results_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Up && Results.SelectedIndex == 0) { QueryBox.Focus(); e.Handled = true; }
        }

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Choose();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    }
}
