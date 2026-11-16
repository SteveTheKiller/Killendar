using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Views
{
    /// <summary>
    /// Twelve months on one screen. A day with appointments is shaded in the accent color, darker
    /// the more it holds, so a busy stretch reads at a glance. Previous and next move a year, and
    /// a click on a day opens that day in the Day view.
    /// </summary>
    internal sealed class YearView : UserControl, ICalendarView
    {
        /// <summary>The darkest shade; a day with this many appointments or more gets it.</summary>
        internal const int MaxLevel = 4;

        private EventStore? _store;
        private DateTime _anchor = DateTime.Today;
        private DateTime? _selectedDay;
        private readonly UniformGrid _months;

        public event Action<CalendarEvent>? EventSelected { add { } remove { } }
        public event Action<DateTime>? DaySelected;
        public event Action<DateTime>? SlotSelected { add { } remove { } }
        public event Action<int>? DensityStepped { add { } remove { } }

        /// <summary>A day was clicked. The shell opens it in the Day view.</summary>
        internal event Action<DateTime>? DayOpened;

        public YearView()
        {
            _months = new UniformGrid { Columns = 4, Rows = RowsFor(4), Margin = new Thickness(12, 8, 12, 12) };
            // One tab stop for the whole year; the arrows move between days inside it.
            KeyboardNavigation.SetTabNavigation(_months, KeyboardNavigationMode.Once);
            KeyboardNavigation.SetDirectionalNavigation(_months, KeyboardNavigationMode.Contained);
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _months,
            };
            Content = scroller;
            SizeChanged += (_, e) =>
            {
                int columns = e.NewSize.Width < 700 ? 3 : 4;
                _months.Columns = columns;
                _months.Rows = RowsFor(columns);
            };
            SetResourceReference(AutomationProperties.NameProperty, "Str_View_Year");
        }

        public DateTime Anchor
        {
            get => _anchor;
            set { _anchor = value; Rebuild(); }
        }

        public void SetSelection(DateTime? day, TimeSpan? timeOfDay)
        {
            if (_selectedDay == day?.Date) return;
            _selectedDay = day?.Date;
            Rebuild();
        }

        public string PeriodLabel => _anchor.Year.ToString(CultureInfo.CurrentCulture);

        public DateTime Step(DateTime from, int direction) => StepBy(from, direction);

        /// <summary>One year back or forward, held inside the years DateTime can show.</summary>
        internal static DateTime StepBy(DateTime from, int direction)
        {
            int year = from.Year + direction;
            if (year < 1 || year > 9999) return from;
            return from.AddYears(direction);
        }

        /// <summary>Rows that fit all twelve months at the given column count.</summary>
        internal static int RowsFor(int columns) => (12 + columns - 1) / columns;

        public void Initialize(EventStore store)
        {
            _store = store;
            Rebuild();
        }

        public void Refresh() => Rebuild();

        /// <summary>How many appointments touch each day of <paramref name="year"/>, capped at
        /// <see cref="MaxLevel"/>. Days with none are left out. An appointment that ends exactly
        /// at midnight does not count on the day after.</summary>
        internal static Dictionary<DateTime, int> BusyLevels(IEnumerable<CalendarEvent> events, int year)
        {
            var first = new DateTime(year, 1, 1);
            var last = year < 9999 ? first.AddYears(1) : DateTime.MaxValue;
            var levels = new Dictionary<DateTime, int>();
            foreach (var ev in events)
            {
                var end = ev.End > ev.Start ? ev.End : (ev.Start < DateTime.MaxValue.AddMinutes(-1) ? ev.Start.AddMinutes(1) : DateTime.MaxValue);
                for (var day = ev.Start.Date < first ? first : ev.Start.Date; day < end && day < last; )
                {
                    levels.TryGetValue(day, out int n);
                    levels[day] = Math.Min(MaxLevel, n + 1);
                    if (day.Date == DateTime.MaxValue.Date) break;
                    day = day.AddDays(1);
                }
            }
            return levels;
        }

        private void Rebuild()
        {
            if (_store == null) return;
            _months.Children.Clear();
            int year = _anchor.Year;
            var from = new DateTime(year, 1, 1);
            var to = year < 9999 ? from.AddYears(1) : DateTime.MaxValue;
            var levels = BusyLevels(ShownCalendars.With(_store.GetInRange(from, to), from, to), year);
            for (int m = 1; m <= 12; m++)
                _months.Children.Add(Month(new DateTime(year, m, 1), levels));
        }

        private FrameworkElement Month(DateTime first, Dictionary<DateTime, int> levels)
        {
            var panel = new StackPanel { Margin = new Thickness(8, 6, 8, 10) };
            var title = new TextBlock
            {
                Text = first.ToString("MMMM", CultureInfo.CurrentCulture),
                FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4),
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            panel.Children.Add(title);

            var grid = new UniformGrid { Columns = 7 };
            var firstDay = WeekStartManager.FirstDay;
            var names = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames;
            for (int i = 0; i < 7; i++)
            {
                var head = new TextBlock
                {
                    Text = names[((int)firstDay + i) % 7], FontSize = 9.5,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2),
                };
                head.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                grid.Children.Add(head);
            }

            int lead = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
            for (int i = 0; i < lead; i++) grid.Children.Add(new Border());
            int days = DateTime.DaysInMonth(first.Year, first.Month);
            for (int d = 1; d <= days; d++)
                grid.Children.Add(DayCell(first.AddDays(d - 1), levels.TryGetValue(first.AddDays(d - 1), out int n) ? n : 0));
            // Six week rows always, so every month in the grid has the same height.
            for (int i = lead + days; i < 42; i++) grid.Children.Add(new Border { Height = 22 });

            panel.Children.Add(grid);
            return panel;
        }

        private FrameworkElement DayCell(DateTime day, int level)
        {
            var cell = new DayCellGrid(() => Open(day)) { Height = 22, Margin = new Thickness(1), Cursor = Cursors.Hand, Focusable = true };
            if (level > 0)
            {
                var shade = new Border { Opacity = 0.18 + 0.18 * level };
                shade.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
                shade.SetResourceReference(Border.CornerRadiusProperty, "SmallCornerRadius");
                cell.Children.Add(shade);
            }
            bool today = day == DateTime.Today;
            if (today || day == _selectedDay)
            {
                var ring = new Border { BorderThickness = new Thickness(today ? 1.5 : 1) };
                ring.SetResourceReference(Border.CornerRadiusProperty, "SmallCornerRadius");
                ring.SetResourceReference(Border.BorderBrushProperty, today ? "SelectionAccent" : "TextBrush");
                cell.Children.Add(ring);
            }
            var label = new TextBlock
            {
                Text = day.Day.ToString(CultureInfo.CurrentCulture), FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = today ? FontWeights.Bold : FontWeights.Normal,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, level >= 3 ? "SelectionFg" : "TextBrush");
            cell.Children.Add(label);
            AutomationProperties.SetName(cell, day.ToString("D", CultureInfo.CurrentCulture));

            cell.MouseLeftButtonUp += (_, e) => { cell.Open(); e.Handled = true; };
            cell.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                cell.Open();
                e.Handled = true;
            };
            return cell;
        }

        private void Open(DateTime day)
        {
            if (DayOpened != null) DayOpened(day);
            else DaySelected?.Invoke(day);
        }

        /// <summary>A day in the year grid. Its own type lets the shell leave the arrow keys to
        /// it, and screen readers hear it as a button.</summary>
        internal sealed class DayCellGrid(Action open) : Grid
        {
            internal void Open() => open();
            protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);

            private sealed class Peer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer,
                System.Windows.Automation.Provider.IInvokeProvider
            {
                public Peer(DayCellGrid owner) : base(owner) { }
                protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
                    System.Windows.Automation.Peers.AutomationControlType.Button;
                protected override string GetClassNameCore() => "YearDay";
                protected override bool IsControlElementCore() => true;
                protected override bool IsContentElementCore() => true;
                public override object GetPattern(System.Windows.Automation.Peers.PatternInterface patternInterface) =>
                    patternInterface == System.Windows.Automation.Peers.PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

                void System.Windows.Automation.Provider.IInvokeProvider.Invoke()
                {
                    if (!IsEnabled()) throw new ElementNotEnabledException();
                    ((DayCellGrid)Owner).Open();
                }
            }
        }
    }
}
