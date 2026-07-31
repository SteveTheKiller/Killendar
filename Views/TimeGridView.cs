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
        // Hover band opacity at rest and while the button is down. RowHoverBrush at full strength
        // is too close to an event chip - the point is to hint at a target, not look like content.
        private const double HoverRest = 0.55;
        private const double HoverPress = 1.0;

        private readonly int _dayCount;
        private EventStore? _store;
        private DateTime _anchor = DateTime.Today;

        private Grid _headerRow = null!;
        private StackPanel _allDayStrip = null!;
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
        /// never the series (Steve, 2026-07-31, "by default just move the occurrence").</summary>
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
                _todayEdge.Visibility = todayFilled ? Visibility.Collapsed : Visibility.Visible;

            if (_todayHead != null)
            {
                if (todayFilled)
                {
                    _todayHead.SetResourceReference(Border.BackgroundProperty, "SelectionBg");
                    _todayHead.BorderThickness = new Thickness(0);
                }
                else
                {
                    // Edges only, matching the column beneath it, so the two read as one marked
                    // strip rather than a lit header over an unlit column.
                    _todayHead.Background = Brushes.Transparent;
                    _todayHead.BorderThickness = new Thickness(1, 1, 1, 0);
                    _todayHead.SetResourceReference(Border.BorderBrushProperty, "PrimaryBrush");
                }
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
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });   // time gutter
            for (int i = 0; i < Days; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        private void Rebuild()
        {
            if (_store == null) return;

            _dayCanvases.Clear();
            // These belong to columns that are about to be discarded; a stale entry would move or
            // show a Border that is no longer in the tree.
            _selectionBands.Clear();
            _todayTint = null;
            _todayEdge = null;
            _todayHead = null;
            _headerRow.Children.Clear();
            _bodyGrid.Children.Clear();
            _bodyGrid.RowDefinitions.Clear();
            _allDayStrip.Children.Clear();

            ApplyColumns(_headerRow);
            ApplyColumns(_bodyGrid);

            var start = RangeStart;
            var today = DateTime.Today;

            // The work-week toggle sits in the header's gutter corner, left of the first day -
            // where the thing it changes is (Steve, 2026-07-31; it was on the rail first, which
            // put it a whole window away from the columns it drops). Rebuilt with the header, so
            // its lit state always matches the columns beside it.
            if (OffersWorkWeekToggle)
            {
                // A labeled button, not a bare glyph (Steve, 2026-07-31, "a button that says
                // 5-Day"). The STYLE is the state (Steve, same day): off it is the gray
                // SurfaceButton, the Cancel treatment; on it is the accent OutlineButton, the
                // OK treatment - which also means the moment it is clicked it shows solid under
                // the pointer (OutlineButton's hover fill) and settles to the accent outline
                // when the mouse leaves. The header rebuilds on every toggle, so the style swap
                // is just picking the right key at build.
                var ww = new Button
                {
                    Content = LocaleManager.Loc("Str_Btn_WorkWeek"),
                    FontSize = 11,
                    Padding = new Thickness(7, 1, 7, 1),
                    Margin = new Thickness(4, 3, 4, 3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = LocaleManager.Loc("Str_TT_WorkWeek"),
                };
                ww.SetResourceReference(StyleProperty,
                    CalendarChrome.WorkWeek ? "OutlineButton" : "SurfaceButton");
                ww.Click += (_, _) => CalendarChrome.WorkWeekToggle?.Invoke();
                Grid.SetColumn(ww, 0);
                _headerRow.Children.Add(ww);
            }

            // ---- day headers ----
            for (int i = 0; i < Days; i++)
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
                    isToday ? "SelectionFg" : "DimTextBrush", 10);
                dow.VerticalAlignment = VerticalAlignment.Center;
                dow.Margin = new Thickness(0, 0, 6, 0);
                // SelectionFg on BOTH the weekday name and the number, matching MonthView. The name
                // used to be PrimaryBrush while the number was SelectionFg, so "THU 30" was printed
                // in two different colours on the one header. (Steve, 2026-07-30.)
                var num = CalendarChrome.Text(date.Day.ToString(),
                    isToday ? "SelectionFg" : "TextBrush", 13,
                    isToday ? FontWeights.Bold : (FontWeight?)null);
                num.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(dow);
                sp.Children.Add(num);

                // Today's header gets the SAME fill-or-edge treatment as its column and as
                // MonthView's cell, rather than being marked by text colour alone. On the
                // TableHeaderBrush strip a white bold label is barely a marker at all - Month
                // fills the whole cell, so Week and Day fill the whole header.
                //
                // The Border wraps the label whether or not it is today: an unwrapped one would sit
                // at a different offset in the row, because the Border's own padding moves it.
                var head = new Border { Child = sp, Padding = new Thickness(0, 2, 0, 2) };

                // The header is the "whole day" surface a time grid has, so clicking it opens the
                // day's agenda in the sidebar - the empty slots below stay the explicit create.
                // Transparent, not null: a null Background gets no mouse events. (Steve, 2026-07-30.)
                head.Background ??= Brushes.Transparent;
                head.Cursor = System.Windows.Input.Cursors.Hand;
                var headDate = date;
                head.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    DaySelected?.Invoke(headDate.Date);
                };
                if (isToday)
                {
                    _todayHead = head;
                    // Rounded on the TOP only, so it reads as a tab on the head of the card rather
                    // than a floating pill - the same 5px the strip's own corners use.
                    head.CornerRadius = new CornerRadius(5, 5, 0, 0);
                }

                Grid.SetColumn(head, i + 1);
                _headerRow.Children.Add(head);
            }

            // ---- all-day strip ----
            var allDay = new List<CalendarEvent>();
            for (int i = 0; i < Days; i++)
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

            for (int i = 0; i < Days; i++)
            {
                var date = start.AddDays(i);
                var canvas = BuildDayCanvas(date, fullHeight);
                Grid.SetColumn(canvas, i + 1);
                Grid.SetRow(canvas, 0);
                _bodyGrid.Children.Add(canvas);
                _dayCanvases.Add(canvas);
            }

            // The columns are freshly built and their bands are all hidden; re-apply the marker so
            // it survives a rebuild (a navigation step, a store refresh, a language change).
            ApplySelectionBand();
        }

        private Canvas BuildDayCanvas(DateTime date, double fullHeight)
        {
            var canvas = new Canvas { Height = fullHeight, Background = Brushes.Transparent, ClipToBounds = true };

            var border = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Height = fullHeight };
            border.Themed(Border.BorderBrushProperty, "CardBorderBrush");
            canvas.Children.Add(border);
            border.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });

            // Hour lines, plus the density subdivisions inside each hour. The interior lines are
            // fainter than the hour line so the hour is still the thing you read the grid by -
            // equal weight turns a quarter-hour grid into visual static.
            int subs = CalendarChrome.Subdivisions;
            double subHeight = CalendarChrome.HourHeight / subs;
            for (int h = 0; h < 24; h++)
            {
                for (int s = 0; s < subs; s++)
                {
                    if (h == 0 && s == 0) continue;         // no line on the very top edge
                    bool onTheHour = s == 0;
                    var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Height = 1 };
                    line.Themed(Border.BorderBrushProperty, "CardBorderBrush");
                    line.Opacity = onTheHour ? 0.6 : 0.25;
                    Canvas.SetTop(line, h * CalendarChrome.HourHeight + s * subHeight);
                    Canvas.SetLeft(line, 0);
                    line.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });
                    canvas.Children.Add(line);
                }
            }

            // Today's column, on exactly the rule Month uses for today's cell: it carries the
            // SelectionBg fill only while NOTHING is selected, and drops to a 1px accent edge as
            // soon as the panel is talking about some other day - so the loudest thing on screen is
            // always the day being edited. This tint used to be unconditional, which is why Week
            // and Day did not match Month. (Steve, 2026-07-30.)
            if (date == DateTime.Today)
            {
                _todayTint = new Border { Height = fullHeight, Opacity = 0.5, IsHitTestVisible = false };
                _todayTint.Themed(Border.BackgroundProperty, "SelectionBg");
                _todayTint.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });
                canvas.Children.Insert(0, _todayTint);

                // Edges only - a full box would draw a line across the bottom of a column that
                // scrolls, which reads as a divider rather than a marker.
                _todayEdge = new Border
                {
                    Height = fullHeight,
                    IsHitTestVisible = false,
                    BorderThickness = new Thickness(1, 0, 1, 0),
                    Visibility = Visibility.Collapsed,
                };
                _todayEdge.Themed(Border.BorderBrushProperty, "PrimaryBrush");
                _todayEdge.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });
                canvas.Children.Insert(1, _todayEdge);
            }

            // Hover band. The column is one Canvas rather than a grid of cells, so there is nothing
            // to light up on its own - this is a single half-hour-tall Border that follows the
            // pointer. Half an hour because that is exactly what a click snaps to: highlighting a
            // whole hour would promise a slot different from the one you actually get.
            //
            // IsHitTestVisible false, or it swallows the clicks it is advertising. Added before
            // PlaceTimedEvents so it sits UNDER the event chips.
            // One subdivision tall, so the bands track the density: at level 0 that is the hour, at
            // level 3 the quarter hour. Half an hour was hardcoded before density existed.
            double slot = CalendarChrome.HourHeight / CalendarChrome.Subdivisions;

            // SELECTED band - the slot the appointment panel is talking about. Same geometry
            // as the hover band and added first, so hovering elsewhere still reads on top of it.
            // Solid SelectionBg with an accent edge, because unlike hover this one persists and has
            // to be findable after the pointer has moved away. (Steve, 2026-07-30.)
            var selected = new Border
            {
                Height = slot,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                // All four edges, not just top and bottom (Steve, 2026-07-31): with open sides
                // the band read as a stripe across the column rather than a marked slot.
                BorderThickness = new Thickness(1),
            };
            selected.Themed(Border.BackgroundProperty, "SelectionBg");
            selected.Themed(Border.BorderBrushProperty, "PrimaryBrush");
            selected.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });
            canvas.Children.Add(selected);
            _selectionBands[date.Date] = selected;

            var hover = new Border
            {
                Height = slot,
                Opacity = HoverRest,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            hover.Themed(Border.BackgroundProperty, "RowHoverBrush");
            hover.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = canvas });
            canvas.Children.Add(hover);

            hover.Height = slot;

            // The band the pointer was last in. MouseMove fires on every pixel, and moving the
            // Border re-arranges the whole column - which on a day with events is real work for a
            // no-op. The band only changes once per subdivision of column height, so most moves
            // are skipped. -1 means "not shown", so re-entering the column always paints.
            int lastBand = -1;

            void MoveHover(double y)
            {
                int band = (int)(Math.Max(0, Math.Min(fullHeight - 1, y)) / slot);
                if (band == lastBand) return;
                lastBand = band;
                Canvas.SetTop(hover, band * slot);
                hover.Visibility = Visibility.Visible;
            }

            canvas.MouseMove += (s, e) => MoveHover(e.GetPosition(canvas).Y);
            canvas.MouseLeave += (s, e) =>
            {
                lastBand = -1;
                hover.Visibility = Visibility.Collapsed;
                hover.Opacity = HoverRest;
            };

            // Click empty space to create at that time, rounded to the nearest half hour.
            canvas.MouseLeftButtonDown += (s, e) =>
            {
                if (e.Handled) return;
                double y = e.GetPosition(canvas).Y;
                // Press: deepen the band under the pointer so the click registers visually before
                // the sidebar opens.
                MoveHover(y);
                hover.Opacity = HoverPress;
                // Snap to the visible subdivision, so a click lands on a line the grid actually
                // drew. Clamped one slot short of midnight rather than at 23:30, which was the old
                // fixed half-hour assumption.
                int snap = CalendarChrome.SnapMinutes;
                double mins = Math.Max(0, Math.Min(24 * 60 - snap, y / CalendarChrome.HourHeight * 60));
                int half = (int)Math.Round(mins / snap);
                SlotSelected?.Invoke(date.Date.AddMinutes(half * snap));
            };
            canvas.MouseLeftButtonUp += (s, e) => hover.Opacity = HoverRest;

            // Right-click offers the same thing. The menu is rebuilt on each press rather than
            // assigned once, because the time it should create at depends on where in the column
            // the pointer is. Assigning on button-DOWN is in time: WPF opens the menu on UP.
            canvas.MouseRightButtonDown += (s, e) =>
            {
                double y = e.GetPosition(canvas).Y;
                // Snap to the visible subdivision, so a click lands on a line the grid actually
                // drew. Clamped one slot short of midnight rather than at 23:30, which was the old
                // fixed half-hour assumption.
                int snap = CalendarChrome.SnapMinutes;
                double mins = Math.Max(0, Math.Min(24 * 60 - snap, y / CalendarChrome.HourHeight * 60));
                int half = (int)Math.Round(mins / snap);
                canvas.ContextMenu = CalendarChrome.DayMenu(
                    date.Date.AddMinutes(half * snap), d => SlotSelected?.Invoke(d));
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
            // Overlap CLUSTERS: a chip only shares its width with events it actually collides
            // with, transitively. The lane count used to be day-global, so one 2 PM collision
            // halved the lone 9 AM standup too (Steve, 2026-07-31). timed is sorted by Start, so
            // a cluster is a run of events whose spans chain together; a gap starts a new one.
            var clusterOf    = new int[timed.Count];
            var clusterLanes = new List<int>();
            int cluster = -1;
            var clusterEnd = DateTime.MinValue;
            for (int i = 0; i < timed.Count; i++)
            {
                if (timed[i].Start >= clusterEnd)
                {
                    cluster++;
                    clusterLanes.Add(0);
                    clusterEnd = timed[i].End;
                }
                else if (timed[i].End > clusterEnd) clusterEnd = timed[i].End;

                clusterOf[i] = cluster;
                clusterLanes[cluster] = Math.Max(clusterLanes[cluster], lanes[i] + 1);
            }

            for (int i = 0; i < timed.Count; i++)
            {
                var ev = timed[i];

                // Clamp to the visible day so a multi-day event renders as a full bar here.
                var visStart = ev.Start < dayStart ? dayStart : ev.Start;
                var visEnd = ev.End > dayEnd ? dayEnd : ev.End;

                double top = (visStart - dayStart).TotalHours * CalendarChrome.HourHeight;
                double height = Math.Max(16, (visEnd - visStart).TotalHours * CalendarChrome.HourHeight - 2);

                int myLanes = Math.Max(1, clusterLanes[clusterOf[i]]);

                // The time prefix only in Day view with no overlap: in a time grid the chip's
                // POSITION already says the time, and in Week's seventh-width columns the prefix
                // was eating the title down to "9:00 Morni" (Steve, 2026-07-31). The tooltip
                // carries the full time either way.
                bool showTime = Days == 1 && myLanes == 1;
                var chip = CalendarChrome.Chip(ev, e => EventSelected?.Invoke(e), compact: false, showTime: showTime);
                chip.Height = height;
                chip.Margin = new Thickness(0);
                // A short appointment's box is thinner than the text plus the chip's 3px vertical
                // padding, so a 15-minute standup clipped its own title. Below 24px the padding
                // goes and the single line gets the whole box.
                if (height < 24) chip.Padding = new Thickness(6, 0, 6, 0);
                Canvas.SetTop(chip, top);

                int lane = lanes[i];
                // Width is a fraction of the column, resolved on layout since the column is star-sized.
                canvas.SizeChanged += (_, _) => PositionChip(chip, canvas, lane, myLanes);
                PositionChip(chip, canvas, lane, myLanes);

                WireDrag(chip, ev, top, canvas);
                canvas.Children.Add(chip);
            }
        }

        /// <summary>
        /// Drag a chip to another slot or another day (Steve, 2026-07-31). The chip follows the
        /// pointer on a RenderTransform; on release the drop is snapped to the visible
        /// subdivision - the same snap a click gets - and raised as EventDropped for the
        /// controller to commit. A press that never travels a drag's worth stays a click, which
        /// Chip itself fires on release. Down and up register handledEventsToo because Chip has
        /// already Handled both to keep them off the day canvas.
        /// </summary>
        private void WireDrag(Border chip, CalendarEvent ev, double chipTop, Canvas canvas)
        {
            var move = new TranslateTransform();
            chip.RenderTransform = move;

            Point start = default;
            bool dragging = false;

            void Reset()
            {
                dragging = false;
                move.X = move.Y = 0;
                chip.Opacity = 1.0;
                Panel.SetZIndex(chip, 0);
                // Undo the in-flight lift - see MouseMove.
                canvas.ClipToBounds = true;
                Panel.SetZIndex(canvas, 0);
            }

            chip.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
            {
                start = e.GetPosition(_bodyGrid);
                dragging = false;
                chip.CaptureMouse();
            }), handledEventsToo: true);

            chip.MouseMove += (_, e) =>
            {
                if (!chip.IsMouseCaptured) return;
                var p = e.GetPosition(_bodyGrid);
                double dx = p.X - start.X, dy = p.Y - start.Y;
                if (!dragging &&
                    (Math.Abs(dx) >= SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(dy) >= SystemParameters.MinimumVerticalDragDistance))
                {
                    dragging = true;
                    chip.Opacity = 0.7;              // lifted, and the grid reads through it
                    Panel.SetZIndex(chip, 99);       // over its neighbors while in flight
                    // The chip lives INSIDE its day canvas, which clips its children and sits at
                    // default z among its sibling columns - so a cross-day drag vanished at its
                    // own column edge (Steve, 2026-07-31). While in flight the CANVAS is unclipped
                    // and lifted; Reset restores both.
                    canvas.ClipToBounds = false;
                    Panel.SetZIndex(canvas, 99);
                }
                if (dragging) { move.X = dx; move.Y = dy; }
            };

            chip.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, e) =>
            {
                if (!chip.IsMouseCaptured) return;

                // Read EVERYTHING before releasing capture: ReleaseMouseCapture fires
                // LostMouseCapture synchronously, and that runs Reset - which zeroes dragging.
                // Checking dragging after the release is why every drop used to snap back
                // committed-nothing (Steve, 2026-07-31).
                bool wasDragging = dragging;
                var p = e.GetPosition(_bodyGrid);
                double dy = p.Y - start.Y;

                chip.ReleaseMouseCapture();          // fires LostMouseCapture -> Reset()
                if (!wasDragging) return;            // a click - Chip's own release handler fires it

                // Column from the pointer, time from where the chip's TOP landed - so the grab
                // point inside the chip does not shift the drop by however far down you grabbed.
                double dayW = Math.Max(1, (_bodyGrid.ActualWidth - 56) / Days);
                int col = (int)Math.Max(0, Math.Min(Days - 1, (p.X - 56) / dayW));

                // Drag snaps at least as fine as the HALF hour, even when the grid is at its
                // loosest. A click follows the visible lines because it creates where a line
                // promised - but a drag moves something that exists, and hour-only drops made
                // half-hour appointments unplaceable at density 0 (Steve, 2026-07-31). A denser
                // grid still gives its finer snap.
                int snap = Math.Min(30, CalendarChrome.SnapMinutes);
                double topMins = (chipTop + dy) / CalendarChrome.HourHeight * 60;
                int mins = (int)Math.Round(topMins / snap) * snap;
                mins = Math.Max(0, Math.Min(24 * 60 - snap, mins));

                EventDropped?.Invoke(ev, RangeStart.AddDays(col).AddMinutes(mins));
            }), handledEventsToo: true);

            // Capture stolen mid-drag (a dialog, focus loss): the chip snaps home, nothing commits.
            chip.LostMouseCapture += (_, _) => Reset();
        }

        private static void PositionChip(Border chip, Canvas canvas, int lane, int laneCount)
        {
            double usable = Math.Max(0, canvas.ActualWidth - 4);
            double w = usable / laneCount;
            chip.Width = Math.Max(0, w - 2);
            Canvas.SetLeft(chip, 2 + lane * w);
        }
    }

    /// <summary>Seven days starting on the culture's first day of the week - or Monday to Friday
    /// when the work-week toggle is on (Steve, 2026-07-31).</summary>
    internal sealed class WeekView : TimeGridView
    {
        public WeekView() : base(7) { }

        protected override int Days => CalendarChrome.WorkWeek ? 5 : 7;

        protected override bool OffersWorkWeekToggle => true;

        protected override DateTime FirstVisibleDay(DateTime anchor)
        {
            if (CalendarChrome.WorkWeek)
            {
                // Monday, whatever day the culture starts its week on - a work week is Mon to
                // Fri. A weekend anchor shows the week it belongs to (the Monday before it).
                int shiftM = ((int)anchor.DayOfWeek + 6) % 7;
                return anchor.Date.AddDays(-shiftM);
            }
            var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int shift = ((int)anchor.DayOfWeek - (int)first + 7) % 7;
            return anchor.Date.AddDays(-shift);
        }

        public override string PeriodLabel
        {
            get
            {
                var s = RangeStart;
                var e = s.AddDays(Days - 1);
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
