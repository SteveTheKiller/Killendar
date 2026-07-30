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

        private EventStore? _store;
        private DateTime _anchor = DateTime.Today;
        private readonly StackPanel _list;

        public event Action<CalendarEvent>? EventSelected;
        public event Action<DateTime>? SlotSelected;

        public AgendaView()
        {
            _list = new StackPanel { Margin = new Thickness(16, 10, 16, 16) };
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _list
            };
        }

        public DateTime Anchor
        {
            get => _anchor;
            set { _anchor = value; Rebuild(); }
        }

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

            var from = _anchor.Date;
            var to = from.AddDays(WindowDays);
            var events = _store.GetInRange(from, to);

            if (events.Count == 0)
            {
                var empty = CalendarChrome.Text(MainWindow.LocStatic("Str_Cal_AgendaEmpty"), "DimTextBrush", 12);
                empty.Margin = new Thickness(0, 24, 0, 0);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                _list.Children.Add(empty);
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
                    bucket = new List<CalendarEvent>();
                    byDay[key] = bucket;
                }
                bucket.Add(ev);
            }

            var today = DateTime.Today;
            foreach (var kv in byDay)
            {
                var date = kv.Key;
                bool isToday = date == today;

                // The day heading doubles as "add something on this day", which is the only
                // empty space an agenda has to offer.
                var head = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 14, 0, 4),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = MainWindow.LocStatic("Str_TT_NewOnDay")
                };
                var headDate = date;
                head.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    SlotSelected?.Invoke(headDate.Date.AddHours(9));
                };
                var dayName = CalendarChrome.Text(
                    isToday ? MainWindow.LocStatic("Str_Cal_Today") : date.ToString("dddd").ToUpperInvariant(),
                    isToday ? "PrimaryBrush" : "MutedTextBrush", 10,
                    isToday ? FontWeights.Bold : (FontWeight?)null);
                dayName.Margin = new Thickness(0, 0, 8, 0);
                dayName.VerticalAlignment = VerticalAlignment.Center;
                var dayDate = CalendarChrome.Text(date.ToString("MMM d"), "TextBrush", 13);
                dayDate.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(dayName);
                head.Children.Add(dayDate);
                _list.Children.Add(head);

                var rule = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 4) };
                rule.Themed(Border.BorderBrushProperty, "CardBorderBrush");
                _list.Children.Add(rule);

                foreach (var ev in kv.Value)
                    _list.Children.Add(BuildRow(ev));
            }
        }

        private Grid BuildRow(CalendarEvent ev)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var time = CalendarChrome.Text(ev.TimeLabel, "MutedTextBrush", 11, null, "Consolas");
            time.VerticalAlignment = VerticalAlignment.Center;
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
