using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Views
{
    /// <summary>
    /// A flat, scrolling list of what is coming up, grouped by day. Shows the 60 days from the
    /// anchor forward: an agenda is for reading ahead, so it deliberately does not page backwards
    /// the way the grid views do - prev/next shifts the window a month at a time.
    /// </summary>
    internal sealed class AgendaView : UserControl, ICalendarView
    {
        private const int WindowDays = 60;
        private const double ColumnMinWidth = 480;
        private const double ColumnGap = 24;

        private EventStore? _store;
        private DateTime _anchor = DateTime.Today;
        private readonly Grid _list;
        private readonly ScrollViewer _scroller;
        private readonly List<StackPanel> _dayGroups = [];
        private int _columns = 1;

        public event Action<CalendarEvent>? EventSelected;
        public event Action<DateTime>? DaySelected;
        public event Action<DateTime>? SlotSelected;

        /// <summary>
        /// Never raised - an agenda list has no hour grid to make denser. Declared with empty
        /// accessors rather than as a field: a field-like event that nothing raises is CS0067,
        /// and the interface still has to be satisfied.
        /// </summary>
        public event Action<int>? DensityStepped { add { } remove { } }

        public AgendaView()
        {
            _list = new Grid { Margin = new Thickness(16, 10, 16, 16) };
            _scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _list
            };
            _scroller.SizeChanged += (_, _) => ArrangeDays();
            Content = _scroller;
        }

        public DateTime Anchor
        {
            get => _anchor;
            set { _anchor = value; Rebuild(); }
        }

        /// <summary>
        /// Accepted and ignored: the agenda is a list of the days that HAVE appointments, so the
        /// day being composed is usually not on screen to mark. Nothing to draw, and pretending
        /// otherwise would mean silently scrolling the list while someone types a date.
        /// </summary>
        public void SetSelection(DateTime? day, TimeSpan? timeOfDay) { }

        public string PeriodLabel
        {
            get
            {
                var end = _anchor.Date.AddDays(WindowDays - 1);
                return $"{_anchor:MMM d} - {end:MMM d, yyyy}";
            }
        }

        public DateTime Step(DateTime from, int direction) => from.AddMonths(direction);

        public void Initialize(EventStore store)
        {
            _store = store;
            Rebuild();
        }

        public void Refresh() => Rebuild();

        private void Rebuild()
        {
            if (_store == null) return;
            _list.Children.Clear();
            _dayGroups.Clear();

            var from = _anchor.Date;
            var to = from.AddDays(WindowDays);
            var events = _store.GetInRange(from, to);

            if (events.Count == 0)
            {
                var empty = CalendarChrome.Text(Services.LocaleManager.Loc("Str_Cal_AgendaEmpty"), "DimTextBrush", 12);
                empty.Margin = new Thickness(0, 24, 0, 0);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                _list.Children.Add(empty);
                ArrangeDays(force: true);
                return;
            }

            // Group by the day each event starts on, clamped to the window so a long-running
            // event that began before the window still shows on the first visible day.
            var byDay = new SortedDictionary<DateTime, List<CalendarEvent>>();
            foreach (var ev in events)
            {
                var key = ev.Start.Date < from ? from : ev.Start.Date;
                if (!byDay.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    byDay[key] = bucket;
                }
                bucket.Add(ev);
            }

            var today = DateTime.Today;
            foreach (var kv in byDay)
            {
                var group = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                _dayGroups.Add(group);
                _list.Children.Add(group);
                var date = kv.Key;
                bool isToday = date == today;

                // The day heading opens that day's agenda in the sidebar, same as a Month cell
                // or a Week/Day column header; the context menu keeps the explicit create.
                // (2026-07-30)
                var head = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 14, 0, 4),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var headDate = date;
                head.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    DaySelected?.Invoke(headDate.Date);
                };
                head.ContextMenu = CalendarChrome.DayMenu(
                    headDate.Date.AddHours(9), d => SlotSelected?.Invoke(d));
                var dayName = CalendarChrome.Text(
                    isToday ? Services.LocaleManager.Loc("Str_Cal_Today") : date.ToString("dddd").ToUpperInvariant(),
                    isToday ? "PrimaryBrush" : "MutedTextBrush", 10,
                    isToday ? FontWeights.Bold : (FontWeight?)null);
                dayName.Margin = new Thickness(0, 0, 8, 0);
                dayName.VerticalAlignment = VerticalAlignment.Center;
                var dayDate = CalendarChrome.Text(date.ToString("MMM d"), "TextBrush", 13);
                dayDate.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(dayName);
                head.Children.Add(dayDate);
                group.Children.Add(head);

                var rule = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 4) };
                rule.Themed(Border.BorderBrushProperty, "CardBorderBrush");
                group.Children.Add(rule);

                foreach (var ev in kv.Value)
                    group.Children.Add(BuildRow(ev));
            }
            ArrangeDays(force: true);
        }

        private void ArrangeDays(bool force = false)
        {
            // Reserve the scrollbar width even when it is hidden, so changing the number of
            // columns cannot repeatedly cross the breakpoint by changing scrollbar visibility.
            double available = _scroller.ActualWidth - SystemParameters.VerticalScrollBarWidth - 32;
            int columns = available >= ColumnMinWidth * 2 + ColumnGap ? 2 : 1;
            if (!force && columns == _columns) return;
            _columns = columns;
            _list.ColumnDefinitions.Clear();
            _list.RowDefinitions.Clear();
            _list.ColumnDefinitions.Add(new ColumnDefinition());
            if (columns == 2)
            {
                _list.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnGap) });
                _list.ColumnDefinitions.Add(new ColumnDefinition());
            }
            for (int i = 0; i < _dayGroups.Count; i++)
            {
                if (i % columns == 0)
                    _list.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(_dayGroups[i], i / columns);
                Grid.SetColumn(_dayGroups[i], (i % columns) * 2);
            }
            if (_dayGroups.Count == 0 && _list.Children.Count != 0)
                Grid.SetColumnSpan(_list.Children[0], _list.ColumnDefinitions.Count);
        }

        private Grid BuildRow(CalendarEvent ev)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var time = CalendarChrome.Text(ev.TimeLabel, "MutedTextBrush", 11, null, "Consolas");
            time.VerticalAlignment = VerticalAlignment.Center;
            time.TextWrapping = TextWrapping.Wrap;
            time.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(time, 0);
            row.Children.Add(time);

            var chip = CalendarChrome.Chip(ev, e => EventSelected?.Invoke(e), compact: false);
            chip.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(chip, 1);
            row.Children.Add(chip);

            return row;
        }
    }
}
