using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Views
{
    /// <summary>
    /// The hour-grid view behind both Week and Day. Built entirely in code (no XAML) because the
    /// layout is a computed grid, not a designed one: a time gutter, N day columns, and a Canvas
    /// per day where appointments are positioned by offset from midnight.
    ///
    /// Overlapping appointments are laid out side by side in "lanes" so neither disappears behind
    /// the other, which is the whole reason this is a Canvas rather than a StackPanel.
    /// </summary>
    internal abstract class TimeGridView : UserControl, ICalendarView
    {
        private readonly int _dayCount;
        private EventStore? _store;
        private DateTime _anchor = DateTime.Today;

        private Grid _headerRow = null!;
        private StackPanel _allDayStrip = null!;
        private Border _allDayHost = null!;
        private Grid _bodyGrid = null!;
        private ScrollViewer _scroller = null!;
        private readonly List<Canvas> _dayCanvases = new List<Canvas>();

        public event Action<CalendarEvent>? EventSelected;
        public event Action<DateTime>? SlotSelected;

        protected TimeGridView(int dayCount)
        {
            _dayCount = dayCount;
            BuildSkeleton();
        }

        /// <summary>First day shown. Week starts on the culture's first day; Day is just the anchor.</summary>
        protected abstract DateTime FirstVisibleDay(DateTime anchor);

        public DateTime Anchor
        {
            get => _anchor;
            set { _anchor = value; Rebuild(); }
        }

        public abstract string PeriodLabel { get; }
        public abstract DateTime Step(DateTime from, int direction);

        protected DateTime RangeStart => FirstVisibleDay(_anchor);
        protected int DayCount => _dayCount;

        public void Initialize(EventStore store)
        {
            _store = store;
            Rebuild();
            // Open on the working day rather than midnight; 7am is above the first meeting for most.
            Dispatcher.BeginInvoke((Action)(() => _scroller.ScrollToVerticalOffset(7 * CalendarChrome.HourHeight)));
        }

        public void Refresh() => Rebuild();

        private void BuildSkeleton()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });   // day headers
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // all-day strip
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // The day-name strip is the head of the calendar card, so its top corners are rounded
            // rather than butted square against the toolbar. Radius 5, not the card's own 4: at 4
            // the rounding is invisible, because TableHeaderBrush and PaneBrush are only #0d0d0d vs
            // #161616 apart.
            //
            // A Grid cannot carry a CornerRadius, so the Grid lives inside a Border that holds the
            // fill, the radius and the bottom rule; that Border replaces the separately overlaid
            // rule this used to draw.
            _headerRow = new Grid();
            var headerHost = new Border
            {
                Child           = _headerRow,
                CornerRadius    = new CornerRadius(5, 5, 0, 0),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            headerHost.Themed(Border.BackgroundProperty, "TableHeaderBrush");
            headerHost.Themed(Border.BorderBrushProperty, "HeaderLineBrush");
            Grid.SetRow(headerHost, 0);
            root.Children.Add(headerHost);

            _allDayStrip = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            _allDayHost = new Border { Child = _allDayStrip, BorderThickness = new Thickness(0, 0, 0, 1), Visibility = Visibility.Collapsed };
            _allDayHost.Themed(Border.BorderBrushProperty, "CardBorderBrush");
            Grid.SetRow(_allDayHost, 1);
            root.Children.Add(_allDayHost);

            _bodyGrid = new Grid();
            _scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _bodyGrid
            };
            Grid.SetRow(_scroller, 2);
            root.Children.Add(_scroller);

            Content = root;
        }

        private void ApplyColumns(Grid g)
        {
            g.ColumnDefinitions.Clear();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });   // time gutter
            for (int i = 0; i < _dayCount; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        private void Rebuild()
        {
            if (_store == null) return;

            _dayCanvases.Clear();
            _headerRow.Children.Clear();
            _bodyGrid.Children.Clear();
            _bodyGrid.RowDefinitions.Clear();
            _allDayStrip.Children.Clear();

            ApplyColumns(_headerRow);
            ApplyColumns(_bodyGrid);

            var start = RangeStart;
            var today = DateTime.Today;

            // ---- day headers ----
            for (int i = 0; i < _dayCount; i++)
            {
                var date = start.AddDays(i);
                bool isToday = date == today;

                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var dow = CalendarChrome.Text(
                    CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(int)date.DayOfWeek].ToUpperInvariant(),
                    isToday ? "PrimaryBrush" : "DimTextBrush", 10);
                dow.VerticalAlignment = VerticalAlignment.Center;
                dow.Margin = new Thickness(0, 0, 6, 0);
                var num = CalendarChrome.Text(date.Day.ToString(),
                    isToday ? "PrimaryBrush" : "TextBrush", 13,
                    isToday ? FontWeights.Bold : (FontWeight?)null);
                num.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(dow);
                sp.Children.Add(num);

                Grid.SetColumn(sp, i + 1);
                _headerRow.Children.Add(sp);
            }

            // ---- all-day strip ----
            var allDay = new List<CalendarEvent>();
            for (int i = 0; i < _dayCount; i++)
            {
                foreach (var ev in _store.GetOnDay(start.AddDays(i)))
                    if (ev.AllDay && !allDay.Contains(ev)) allDay.Add(ev);
            }
            if (allDay.Count > 0)
            {
                _allDayHost.Visibility = Visibility.Visible;
                foreach (var ev in allDay)
                {
                    var chip = CalendarChrome.Chip(ev, e => EventSelected?.Invoke(e), compact: false);
                    chip.Margin = new Thickness(58, 1, 6, 1);
                    _allDayStrip.Children.Add(chip);
                }
            }
            else
            {
                _allDayHost.Visibility = Visibility.Collapsed;
            }

            // ---- hour rows + gutter ----
            double fullHeight = 24 * CalendarChrome.HourHeight;
            _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(fullHeight) });

            var gutter = new Canvas { Height = fullHeight };
            for (int h = 0; h < 24; h++)
            {
                var lbl = CalendarChrome.Text(CalendarChrome.HourLabel(h), "DimTextBrush", 10, null, "Consolas");
                Canvas.SetTop(lbl, h * CalendarChrome.HourHeight - 6);
                Canvas.SetRight(lbl, 8);
                lbl.Width = 48;
                lbl.TextAlignment = TextAlignment.Right;
                if (h > 0) gutter.Children.Add(lbl);
            }
            Grid.SetColumn(gutter, 0);
            Grid.SetRow(gutter, 0);
            _bodyGrid.Children.Add(gutter);

            for (int i = 0; i < _dayCount; i++)
            {
                var date = start.AddDays(i);
                var canvas = BuildDayCanvas(date, fullHeight);
                Grid.SetColumn(canvas, i + 1);
                Grid.SetRow(canvas, 0);
                _bodyGrid.Children.Add(canvas);
                _dayCanvases.Add(canvas);
            }
        }

        private Canvas BuildDayCanvas(DateTime date, double fullHeight)
        {
            var canvas = new Canvas { Height = fullHeight, Background = Brushes.Transparent, ClipToBounds = true };

            var border = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Height = fullHeight };
            border.Themed(Border.BorderBrushProperty, "CardBorderBrush");
            canvas.Children.Add(border);
            border.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });

            // Hour lines
            for (int h = 1; h < 24; h++)
            {
                var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Height = 1 };
                line.Themed(Border.BorderBrushProperty, "CardBorderBrush");
                line.Opacity = 0.6;
                Canvas.SetTop(line, h * CalendarChrome.HourHeight);
                Canvas.SetLeft(line, 0);
                line.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });
                canvas.Children.Add(line);
            }

            // Today's date gets a tinted backdrop so the current column is obvious.
            if (date == DateTime.Today)
            {
                var tint = new Border { Height = fullHeight, Opacity = 0.5, IsHitTestVisible = false };
                tint.Themed(Border.BackgroundProperty, "RowSelectedBrush");
                tint.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });
                canvas.Children.Insert(0, tint);
            }

            // Click empty space to create at that time, rounded to the nearest half hour.
            canvas.MouseLeftButtonDown += (s, e) =>
            {
                if (e.Handled) return;
                double y = e.GetPosition(canvas).Y;
                double hours = Math.Max(0, Math.Min(23.5, y / CalendarChrome.HourHeight));
                int half = (int)Math.Round(hours * 2);
                SlotSelected?.Invoke(date.Date.AddMinutes(half * 30));
            };

            PlaceTimedEvents(canvas, date);
            return canvas;
        }

        private void PlaceTimedEvents(Canvas canvas, DateTime date)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);

            var timed = new List<CalendarEvent>();
            foreach (var ev in _store!.GetOnDay(date))
                if (!ev.AllDay) timed.Add(ev);
            if (timed.Count == 0) return;

            timed.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Lane assignment: an event takes the first lane whose last event has already ended.
            var laneEnds = new List<DateTime>();
            var lanes = new int[timed.Count];
            for (int i = 0; i < timed.Count; i++)
            {
                var ev = timed[i];
                int lane = -1;
                for (int l = 0; l < laneEnds.Count; l++)
                {
                    if (laneEnds[l] <= ev.Start) { lane = l; break; }
                }
                if (lane < 0) { laneEnds.Add(ev.End); lane = laneEnds.Count - 1; }
                else laneEnds[lane] = ev.End;
                lanes[i] = lane;
            }
            int laneCount = Math.Max(1, laneEnds.Count);

            for (int i = 0; i < timed.Count; i++)
            {
                var ev = timed[i];

                // Clamp to the visible day so a multi-day event renders as a full bar here.
                var visStart = ev.Start < dayStart ? dayStart : ev.Start;
                var visEnd = ev.End > dayEnd ? dayEnd : ev.End;

                double top = (visStart - dayStart).TotalHours * CalendarChrome.HourHeight;
                double height = Math.Max(16, (visEnd - visStart).TotalHours * CalendarChrome.HourHeight - 2);

                var chip = CalendarChrome.Chip(ev, e => EventSelected?.Invoke(e), compact: false, showTime: true);
                chip.Height = height;
                chip.Margin = new Thickness(0);
                Canvas.SetTop(chip, top);

                int lane = lanes[i];
                // Width is a fraction of the column, resolved on layout since the column is star-sized.
                canvas.SizeChanged += (_, _) => PositionChip(chip, canvas, lane, laneCount);
                PositionChip(chip, canvas, lane, laneCount);

                canvas.Children.Add(chip);
            }
        }

        private static void PositionChip(Border chip, Canvas canvas, int lane, int laneCount)
        {
            double usable = Math.Max(0, canvas.ActualWidth - 4);
            double w = usable / laneCount;
            chip.Width = Math.Max(0, w - 2);
            Canvas.SetLeft(chip, 2 + lane * w);
        }
    }

    /// <summary>Seven days, starting on the culture's first day of the week.</summary>
    internal sealed class WeekView : TimeGridView
    {
        public WeekView() : base(7) { }

        protected override DateTime FirstVisibleDay(DateTime anchor)
        {
            var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int shift = ((int)anchor.DayOfWeek - (int)first + 7) % 7;
            return anchor.Date.AddDays(-shift);
        }

        public override string PeriodLabel
        {
            get
            {
                var s = RangeStart;
                var e = s.AddDays(6);
                return s.Year == e.Year && s.Month == e.Month
                    ? $"{s:MMM d} - {e:d}, {e:yyyy}"
                    : $"{s:MMM d} - {e:MMM d}, {e:yyyy}";
            }
        }

        public override DateTime Step(DateTime from, int direction) => from.AddDays(7 * direction);
    }

    /// <summary>A single day.</summary>
    internal sealed class DayView : TimeGridView
    {
        public DayView() : base(1) { }

        protected override DateTime FirstVisibleDay(DateTime anchor) => anchor.Date;

        public override string PeriodLabel => Anchor.ToString("dddd, MMMM d, yyyy");

        public override DateTime Step(DateTime from, int direction) => from.AddDays(direction);
    }
}
