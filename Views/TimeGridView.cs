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
    internal abstract partial class TimeGridView : UserControl, ICalendarView
    {
        // Hover band opacity at rest and while the button is down. RowHoverBrush at full strength
        // is too close to an event chip - the point is to hint at a target, not look like content.
        private const double HoverRest = 0.55;
        private const double HoverPress = 1.0;

        private readonly int _dayCount;
        private EventStore? _store;
        private DateTime _anchor = DateTime.Today;

        private Grid _headerRow = null!;
        private RowDefinition _headerDefinition = null!;
        private Grid _allDayStrip = null!;
        private Border _allDayHost = null!;
        private Grid _bodyGrid = null!;
        private ScrollViewer _scroller = null!;
        private readonly List<Canvas> _dayCanvases = [];

        public event Action<CalendarEvent>? EventSelected;
        public event Action<DateTime>? DaySelected;
        public event Action<DateTime>? SlotSelected;
        public event Action<int>? DensityStepped;

        /// <summary>A chip was dragged to a new slot: the appointment and the start it was
        /// dropped on. The controller commits it - for a series date that means an override,
        /// never the series (2026-07-31).</summary>
        public event Action<CalendarEvent, DateTime>? EventDropped;

        protected TimeGridView(int dayCount)
        {
            _dayCount = dayCount;
            BuildSkeleton();

            // Ctrl+wheel changes density; plain wheel keeps scrolling the day. Preview, so it is
            // seen before the ScrollViewer consumes it, and Handled so the grid does not also
            // scroll while zooming.
            PreviewMouseWheel += (_, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                e.Handled = true;
                DensityStepped?.Invoke(e.Delta > 0 ? +1 : -1);
            };
        }

        /// <summary>First day shown. Week starts on the culture's first day; Day is just the anchor.</summary>
        protected abstract DateTime FirstVisibleDay(DateTime anchor);

        /// <summary>How many day columns to build. Virtual, and read on every rebuild rather than
        /// captured, so WeekView can answer 5 or 7 as the work-week toggle changes without the
        /// view being reconstructed.</summary>
        protected virtual int Days => _dayCount;

        /// <summary>True where the work-week toggle belongs in the header corner - Week only.</summary>
        protected virtual bool OffersWorkWeekToggle => false;

        public DateTime Anchor
        {
            get => _anchor;
            set { _anchor = value; Rebuild(); }
        }

        // date -> that column's selection band. Rebuilt with the columns.
        private readonly Dictionary<DateTime, Border> _selectionBands = [];

        // Today's decoration, when today is in range. Null otherwise.
        private Border? _todayTint;   // the column fill
        private Border? _todayEdge;   // the column's accent edges, when another day is selected
        private Border? _todayHead;   // the day header at the top of that column
        private TextBlock? _todayDow;
        private TextBlock? _todayNum;

        private DateTime? _selDay;
        private TimeSpan? _selTime;

        /// <summary>
        /// Marks the half hour the appointment panel is talking about. Needs BOTH parts: with no
        /// time (all-day, or a half-typed time box) there is no slot to mark, so the band is hidden
        /// rather than parked at midnight, which would point at a real slot nobody chose.
        /// </summary>
        public void SetSelection(DateTime? day, TimeSpan? timeOfDay)
        {
            if (day?.Date == _selDay && timeOfDay == _selTime) return;
            _selDay = day?.Date;
            _selTime = timeOfDay;
            ApplySelectionBand();
        }

        /// <summary>
        /// Shows the band on the one column that matches, hides it everywhere else. Only the two
        /// affected columns actually change, but there are at most seven and each is one Border, so
        /// this stays cheap enough to run on a keystroke.
        /// </summary>
        private void ApplySelectionBand()
        {
            int snap = CalendarChrome.SnapMinutes;
            double slot = CalendarChrome.HourHeight / CalendarChrome.Subdivisions;
            foreach (var kv in _selectionBands)
            {
                bool on = _selDay != null && _selTime != null && kv.Key == _selDay;
                if (on)
                {
                    // Snap to the visible subdivision, the same granularity a click resolves to, so
                    // the band never promises a slot different from the one the click would give.
                    int band = (int)(_selTime!.Value.TotalMinutes / snap);
                    band = Math.Max(0, Math.Min(24 * CalendarChrome.Subdivisions - 1, band));
                    Canvas.SetTop(kv.Value, band * slot);
                    kv.Value.Height = slot;
                }
                kv.Value.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }

            // Today's column AND its header, on Month's rule: fill only while nothing is selected,
            // otherwise the accent edge. Selecting today itself keeps the fill - it is the
            // selected day.
            bool todayFilled = _selDay == null || _selDay == DateTime.Today;
            if (_todayTint != null)
                _todayTint.Visibility = todayFilled ? Visibility.Visible : Visibility.Collapsed;
            if (_todayEdge != null)
                _todayEdge.Visibility = Visibility.Collapsed;

            if (_todayHead != null)
            {
                _todayHead.Background = Brushes.Transparent;
                _todayHead.BorderThickness = new Thickness(0);
                _todayDow?.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
                _todayNum?.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            }
        }

        public abstract string PeriodLabel { get; }
        public abstract DateTime Step(DateTime from, int direction);

        protected DateTime RangeStart => FirstVisibleDay(_anchor);

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
            _headerDefinition = new RowDefinition { Height = new GridLength(30) };
            root.RowDefinitions.Add(_headerDefinition);                                  // day headers
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // all-day strip
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Week/Day use Month's surface rule: labels live directly on the app background,
            // while the actual calendar body owns the pane fill and grain beneath them.
            var bodySurface = new Border { BorderThickness = new Thickness(0) };
            bodySurface.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
            bodySurface.SetResourceReference(Border.CornerRadiusProperty, "PanelCornerRadius");
            bodySurface.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = TryFindResource("PaneShadowOpacity") is double opacity ? opacity : 0.6,
            };
            Grid.SetRow(bodySurface, 1);
            Grid.SetRowSpan(bodySurface, 2);
            root.Children.Add(bodySurface);

            var bodyGrain = new Border { IsHitTestVisible = false };
            bodyGrain.SetResourceReference(Border.BackgroundProperty, "GrainTileBrush");
            bodyGrain.SetResourceReference(OpacityProperty, "GrainOpacity");
            bodyGrain.SetResourceReference(Border.CornerRadiusProperty, "PanelCornerRadius");
            Grid.SetRow(bodyGrain, 1);
            Grid.SetRowSpan(bodyGrain, 2);
            root.Children.Add(bodyGrain);

            // Like Month, weekday names are a quiet axis on the window surface rather than a
            // second card sitting on top of the time grid.
            _headerRow = new Grid();
            var headerHost = new Border
            {
                Child           = _headerRow,
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            Grid.SetRow(headerHost, 0);
            root.Children.Add(headerHost);

            _allDayStrip = new Grid { Margin = new Thickness(0, 2, 0, 2) };
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

            // The body scrolls vertically and its scrollbar is effectively always visible (24h of
            // rows), which makes the body's star columns narrower than the header's by exactly the
            // bar's width - every day boundary drifted right of its header, worst at SAT. The
            // header grid gets a matching right inset, measured from the themed bar itself rather
            // than SystemParameters (the template is not the system metric - KillerShell's rule).
            _scroller.ScrollChanged += (_, _) => SyncHeaderInset();
            _scroller.SizeChanged   += (_, _) => SyncHeaderInset();
            _scroller.Loaded        += (_, _) => SyncHeaderInset();

            Content = root;
        }

        private void SyncHeaderInset()
        {
            double inset = 0;
            if (_scroller.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                var bar = FindVerticalBar(_scroller);
                inset = bar?.ActualWidth ?? SystemParameters.VerticalScrollBarWidth;
            }
            var m = _headerRow.Margin;
            if (Math.Abs(m.Right - inset) < 0.5) return;   // no churn on every layout pass
            _headerRow.Margin = new Thickness(m.Left, m.Top, inset, m.Bottom);
        }

        // A ScrollViewer has two scrollbars, so the orientation is checked rather than assumed.
        private static System.Windows.Controls.Primitives.ScrollBar? FindVerticalBar(DependencyObject root)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is System.Windows.Controls.Primitives.ScrollBar sb &&
                    sb.Orientation == Orientation.Vertical) return sb;
                var deeper = FindVerticalBar(c);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void ApplyColumns(Grid g)
        {
            g.ColumnDefinitions.Clear();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });   // time gutter
            for (int i = 0; i < Days; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
    }
}
