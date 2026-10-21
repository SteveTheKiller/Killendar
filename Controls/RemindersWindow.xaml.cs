using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>The due reminders, each with Snooze and Dismiss, or, while the calendar is locked,
    /// only how many there are. The window decides nothing: the shell stores what was chosen and
    /// calls ShowReminders again with what is still due.</summary>
    public partial class RemindersWindow : Window
    {
        public event Action<DueReminder>? Dismissed;
        public event Action<IReadOnlyList<DueReminder>>? DismissedAll;
        public event Action<DueReminder, int>? Snoozed;
        /// <summary>Open Killendar, at the reminder's day when one was clicked.</summary>
        public event Action<DueReminder?>? OpenRequested;

        private IReadOnlyList<DueReminder> _due = [];

        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        public RemindersWindow()
        {
            InitializeComponent();
            TitleIcon.Source = BitmapDecoder.Create(new Uri("pack://application:,,,/Resources/kd-icon.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            SizeChanged += (_, _) => PlaceInCorner();
        }

        /// <summary>Bottom right of the work area, where notifications appear.</summary>
        private void PlaceInCorner()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8;
        }

        public void ShowReminders(IReadOnlyList<DueReminder> due, Func<DueReminder, string> describe)
        {
            _due = due;
            Rows.Children.Clear();
            DismissAllButton.Visibility = due.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var reminder in due) Rows.Children.Add(Row(reminder, describe(reminder)));
        }

        /// <summary>Locked: nothing from the calendar, only the count.</summary>
        public void ShowLockedCount(int count)
        {
            _due = [];
            Rows.Children.Clear();
            DismissAllButton.Visibility = Visibility.Collapsed;
            Rows.Children.Add(new TextBlock
            {
                Text = string.Format(Loc("Str_Rem_Locked"), count),
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 8),
            });
        }

        private FrameworkElement Row(DueReminder reminder, string when)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
            string title = string.IsNullOrWhiteSpace(reminder.Title) ? Loc("Str_Cal_NoTitle") : reminder.Title;
            text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var detail = new TextBlock { Text = when, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            text.Children.Add(detail);
            text.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke(reminder);
            AutomationPropertiesName(text, title + ", " + when);
            row.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var snooze = new Button { Content = Loc("Str_Rem_Snooze"), Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 6, 0) };
            snooze.SetResourceReference(StyleProperty, "SurfaceButton");
            snooze.Click += (_, _) => SnoozeMenu(snooze, reminder).IsOpen = true;
            var dismiss = new Button { Content = Loc("Str_Rem_Dismiss"), Height = 26, Padding = new Thickness(10, 0, 10, 0) };
            dismiss.SetResourceReference(StyleProperty, "OutlineButton");
            dismiss.Click += (_, _) => Dismissed?.Invoke(reminder);
            buttons.Children.Add(snooze);
            buttons.Children.Add(dismiss);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            return row;
        }

        private ContextMenu SnoozeMenu(Button anchor, DueReminder reminder)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            foreach (int minutes in Reminders.SnoozeChoices)
            {
                var item = new MenuItem
                {
                    Header = minutes == 60 ? Loc("Str_Rem_SnoozeHour") : string.Format(Loc("Str_Rem_SnoozeFor"), minutes),
                };
                item.Click += (_, _) => Snoozed?.Invoke(reminder, minutes);
                menu.Items.Add(item);
            }
            return menu;
        }

        private static void AutomationPropertiesName(DependencyObject element, string name) =>
            System.Windows.Automation.AutomationProperties.SetName(element, name);

        private void Open_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(_due.FirstOrDefault());

        private void DismissAll_Click(object sender, RoutedEventArgs e) => DismissedAll?.Invoke(_due);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    }
}
