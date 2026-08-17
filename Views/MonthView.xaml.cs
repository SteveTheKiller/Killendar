using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Views
{
    public partial class MonthView : UserControl, ICalendarView
    {
        private EventStore? _store;
        private DateTime _anchor = DateTime.Today;
        private int _visibleWeeks;
        private int _lastRollingWeeks = 4;
        private DateTime _rollingStart;
        private bool _zoomStartedFromCalendar;
        private int _zoomReturnWeeks;
        private DateTime _gridStart;
        private int _gridRows;
        private readonly System.Collections.Generic.Dictionary<DateTime, double> _dayScrollOffsets = [];
        private readonly System.Collections.Generic.HashSet<Guid> _shrunkAppointments = [];
        private const string ShrunkAppointmentsSetting = "MonthShrunkAppointments";

        public event Action<CalendarEvent>? EventSelected;
        public event Action<DateTime>? DaySelected;
        public event Action<DateTime>? SlotSelected;
        internal event Action? ZoomChanged;

        /// <summary>A chip dragged to another day (2026-07-31). Month keeps the clock and
        /// moves the DATE; the controller commits - an occurrence as an override, never the series.</summary>
        public event Action<CalendarEvent, DateTime>? EventDropped;

        /// <summary>
        /// Never raised - a month cell is a day, with no hour grid to make denser. Declared with
        /// empty accessors rather than as a field: a field-like event that nothing raises is
        /// CS0067, and the interface still has to be satisfied.
        /// </summary>
        // Month uses the shared density setting as appointment detail rather than hour height.
        // The rail button refreshes through the controller; Ctrl+wheel over Month remains reserved
        // for its separate visible-weeks zoom.
        public event Action<int>? DensityStepped { add { } remove { } }

        public MonthView()
        {
            InitializeComponent();
            if (int.TryParse(Settings.Get("MonthVisibleWeeks"), out int saved))
                _lastRollingWeeks = Math.Max(1, Math.Min(6, saved));
            _visibleWeeks = string.Equals(Settings.Get("MonthViewMode"), "Rolling",
                                StringComparison.OrdinalIgnoreCase)
                ? _lastRollingWeeks : 0;
            _rollingStart = _visibleWeeks > 0 ? StartOfWeek(_anchor) : StartOfGrid(_anchor);

            foreach (string value in (Settings.Get(ShrunkAppointmentsSetting) ?? "")
                         .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (Guid.TryParse(value, out var id)) _shrunkAppointments.Add(id);

            PreviewMouseWheel += (_, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                bool enteringFromCalendar = _visibleWeeks == 0;
                int current;
                if (_visibleWeeks > 0)
                {
                    current = _visibleWeeks;
                }
                else
                {
                    // Entering zoom from a calendar month must retain that month's first grid
                    // week. Deriving the start from Anchor instead jumped to the middle week.
                    current = NaturalRowCount(_anchor);
                    _zoomStartedFromCalendar = true;
                    _zoomReturnWeeks = current;
                    _rollingStart = StartOfGrid(_anchor);
                }

                int next = Math.Max(1, Math.Min(6, current + (e.Delta > 0 ? -1 : 1)));
                if (enteringFromCalendar && next == current)
                {
                    _zoomStartedFromCalendar = false;
                    e.Handled = true;
                    return;
                }
                if (_zoomStartedFromCalendar && next == _zoomReturnWeeks)
                {
                    // The temporary zoom has returned to the calendar's natural size. Drop the
                    // rolling range completely so the exact calendar-month silhouette returns.
                    _visibleWeeks = 0;
                    _zoomStartedFromCalendar = false;
                    Settings.Set("MonthViewMode", "Calendar");
                }
                else
                {
                    _visibleWeeks = next;
                    Settings.Set("MonthViewMode", "Rolling");
                }
                if (_visibleWeeks > 0)
                {
                    _lastRollingWeeks = _visibleWeeks;
                    Settings.Set("MonthVisibleWeeks", _visibleWeeks.ToString(CultureInfo.InvariantCulture));
                }
                Rebuild();
                ZoomChanged?.Invoke();
                e.Handled = true;
            };
        }

        internal bool IsRollingMode => _visibleWeeks > 0;

        internal void SetRollingMode(bool rolling)
        {
            if (rolling == IsRollingMode) return;
            if (rolling)
            {
                _visibleWeeks = Math.Max(1, Math.Min(6, _lastRollingWeeks));
                _rollingStart = StartOfWeek(_anchor);
            }
            else
            {
                if (_visibleWeeks > 0) _lastRollingWeeks = _visibleWeeks;
                _visibleWeeks = 0;
            }

            _zoomStartedFromCalendar = false;

            Settings.Set("MonthVisibleWeeks", _lastRollingWeeks.ToString(CultureInfo.InvariantCulture));
            Settings.Set("MonthViewMode", rolling ? "Rolling" : "Calendar");
            Rebuild();
            ZoomChanged?.Invoke();
        }

        public void Initialize(EventStore store)
        {
            _store = store;
            Rebuild();
        }

        public DateTime Anchor
        {
            get => _anchor;
            set
            {
                bool moved = value.Date != _anchor.Date;
                _anchor = value;
                if (_visibleWeeks > 0 && moved)
                    _rollingStart = StartOfWeek(value);
                Rebuild();
            }
        }

        private DateTime? _selectedDay;

        /// <summary>
        /// The day the appointment panel is editing. The time is ignored here - a month cell is a
        /// day, so a keystroke in the TIME box must not repaint anything.
        ///
        /// Repaints only the cells that can have changed rather than calling Rebuild. This is
        /// driven by TextChanged, so it fires on every keystroke, and rebuilding a 42-cell grid
        /// (each cell re-querying the store for its events) per keystroke would be exactly the kind
        /// of per-input layout churn that made the toolbar lag.
        /// </summary>
        public void SetSelection(DateTime? day, TimeSpan? timeOfDay)
        {
            var v = day?.Date;
            if (v == _selectedDay) return;
            var was = _selectedDay;
            _selectedDay = v;
            RepaintDay(was);
            RepaintDay(v);
            // Today as well. Its appearance depends on whether ANYTHING is selected - it holds the
            // fill only while the selection is empty - so it changes on the null-to-date and
            // date-to-null transitions even though it is neither the old nor the new day. Missing
            // this is what put two solid cells on screen at once. (2026-07-30)
            if (was == null || v == null) RepaintDay(DateTime.Today);
        }

        public string PeriodLabel
        {
            get
            {
                if (_visibleWeeks <= 0) return _anchor.ToString("MMMM yyyy");
                var start = _rollingStart;
                var end = start.AddDays(_visibleWeeks * 7 - 1);
                return start.Year == end.Year && start.Month == end.Month
                    ? $"{start:MMMM d} - {end:d}, {end:yyyy}"
                    : $"{start:MMM d} - {end:MMM d}, {end:yyyy}";
            }
        }

        public DateTime Step(DateTime from, int direction) => _visibleWeeks > 0
            ? _rollingStart.AddDays(7 * _visibleWeeks * direction)
            : from.AddMonths(direction);

        public void Refresh() => Rebuild();

        /// <summary>First day of the week for the current culture, so this is not hardcoded to Sunday.</summary>
        private static DayOfWeek FirstDay => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

        private static DateTime StartOfGrid(DateTime anchor)
        {
            var first = new DateTime(anchor.Year, anchor.Month, 1);
            int shift = ((int)first.DayOfWeek - (int)FirstDay + 7) % 7;
            return first.AddDays(-shift);
        }

        private static DateTime StartOfWeek(DateTime anchor)
        {
            int shift = ((int)anchor.DayOfWeek - (int)FirstDay + 7) % 7;
            return anchor.Date.AddDays(-shift);
        }

        private static int NaturalRowCount(DateTime anchor)
        {
            var start = StartOfGrid(anchor);
            var last = new DateTime(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1);
            return (int)Math.Ceiling(((last - start).TotalDays + 1) / 7.0);
        }

        private void Rebuild()
        {
            if (_store == null) return;

            BuildWeekdayHeader();

            // The maps point at Borders that are about to be discarded. Stale entries would make
            // RepaintDay color a cell that is no longer in the tree.
            _cells.Clear();
            _rings.Clear();

            CalGrid.Children.Clear();
            CalGrid.RowDefinitions.Clear();
            CalGrid.ColumnDefinitions.Clear();
            for (int c = 0; c < 7; c++)
                CalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var start = _visibleWeeks > 0 ? _rollingStart : StartOfGrid(_anchor);
            int rows = _visibleWeeks > 0 ? _visibleWeeks : NaturalRowCount(_anchor);
            _gridStart = start;
            _gridRows = rows;
            for (int r = 0; r < rows; r++)
                CalGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var today = DateTime.Today;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < 7; c++)
                {
                    var date = start.AddDays(r * 7 + c);
                    bool inMonth = date.Month == _anchor.Month;
                    // Draw separators only where another real day of this month follows. Edges
                    // against spillover dates are the silhouette perimeter, not grid lines; a
                    // stroke there puts the unwanted box back around the hollowed-out days.
                    bool rightSeparator = inMonth && c < 6 &&
                        start.AddDays(r * 7 + c + 1).Month == _anchor.Month;
                    bool bottomSeparator = inMonth && r < rows - 1 &&
                        start.AddDays((r + 1) * 7 + c).Month == _anchor.Month;
                    var cell = BuildDayCell(date, inMonth, date == today,
                                            c, rightSeparator, bottomSeparator);
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    CalGrid.Children.Add(cell);
                }
            }
            UpdateMonthSilhouette();
        }

        private void MonthRoot_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMonthSilhouette();

        private void UpdateMonthSilhouette()
        {
            double width = MonthRoot.ActualWidth;
            double height = MonthRoot.ActualHeight;
            if (width <= 0 || height <= 26 || _gridRows <= 0) return;

            const double headerHeight = 26;
            double cellWidth = width / 7.0;
            double cellHeight = (height - headerHeight) / _gridRows;
            var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
            for (int row = 0; row < _gridRows; row++)
            for (int column = 0; column < 7; column++)
            {
                var date = _gridStart.AddDays(row * 7 + column);
                if (date.Month != _anchor.Month) continue;
                geometry.Children.Add(new RectangleGeometry(
                    new Rect(column * cellWidth, headerHeight + row * cellHeight,
                             cellWidth, cellHeight)));
            }

            MonthSilhouette.Data = geometry;
            MonthSilhouetteGrain.Data = geometry;
        }

        private void BuildWeekdayHeader()
        {
            WeekdayHeader.Children.Clear();
            WeekdayHeader.ColumnDefinitions.Clear();
            for (int c = 0; c < 7; c++)
                WeekdayHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
            for (int c = 0; c < 7; c++)
            {
                var dow = (DayOfWeek)(((int)FirstDay + c) % 7);
                var tb = CalendarChrome.Text(names[(int)dow].ToUpperInvariant(), "DimTextBrush", 10);
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                tb.VerticalAlignment = VerticalAlignment.Center;
                var headerCell = new Border { Child = tb, Background = Brushes.Transparent };
                Grid.SetColumn(headerCell, c);
                WeekdayHeader.Children.Add(headerCell);
            }
        }

        private Border BuildDayCell(DateTime date, bool inMonth, bool isToday,
                                    int displayColumn,
                                    bool rightSeparator, bool bottomSeparator)
        {
            // Every event uses the same per-day stack. Multi-day appointments used to live in a
            // second Grid overlay spanning columns, which could neither scroll with the cell nor
            // share its lanes; that is what caused overlap and vertical misalignment. Repeating a
            // multi-day appointment in each covered day is less ornamental and much more honest:
            // one grid, one sorter, one scrollbar, one density system.
            var events = _store!.GetOnDay(date)
                .OrderBy(e => !e.AllDay)
                .ThenBy(e => e.Start)
                .ThenBy(e => e.End)
                .ToList();

            var cell = new Border
            {
                BorderThickness = inMonth
                    ? new Thickness(0, 0, rightSeparator ? 1 : 0, bottomSeparator ? 1 : 0)
                    : new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = date
            };
            cell.Themed(Border.BorderBrushProperty, "CardBorderBrush");

            // Days outside the current month sit back. The SELECTED day - the one the appointment
            // panel is talking about - gets the SelectionBg fill, and today gets it too whenever
            // nothing is selected. When the two differ, today falls back to a 1px accent ring so
            // the strongest mark on the grid is always the day you are actually editing.
            //
            // In-month days carry NO fill of their own. They used to paint PaneBrush, which is the
            // same color as the card underneath - so it looked identical while quietly covering
            // the card's film grain, and only the semi-transparent out-of-month cells (RowAltBrush
            // is #14FFFFFF) showed any texture. Transparent, NOT null: a Border with a null
            // Background receives no mouse events, and the cell has to stay clickable.
            //
            // SelectionBg is the family's "this one is active" fill - the same brush behind a
            // selected tool, tab or menu item. Today used to be the neutral SurfaceBrush, which is
            // the same brush the PRESS state uses, so it looked permanently half-clicked.
            // (2026-07-30)
            var content = new Grid { Margin = new Thickness(4, 3, 4, 3) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // The foreground follows the CELL STATE in ApplyRest/ApplyPointerState. It cannot be
            // chosen from isToday alone: today loses its fill while another day is selected, and
            // every selected non-today cell gains that fill. Ectoplasm exposed both failures at
            // once because its SelectionFg is intentionally near-black against yellow.
            var dayNum = CalendarChrome.Text(
                date.Day.ToString(),
                inMonth ? "TextBrush" : "DimTextBrush",
                11,
                isToday ? FontWeights.Bold : (FontWeight?)null);
            dayNum.HorizontalAlignment = HorizontalAlignment.Right;
            dayNum.Margin = new Thickness(0, 1, 2, 3);
            Grid.SetRow(dayNum, 0);
            content.Children.Add(dayNum);

            var eventList = new StackPanel();
            foreach (var ev in events)
            {
                var chip = BuildMonthChip(ev);
                WireDrag(chip, ev, cell);
                eventList.Children.Add(chip);
            }

            var eventScroll = new ScrollViewer
            {
                Content = eventList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly,
            };
            Grid.SetRow(eventScroll, 1);
            content.Children.Add(eventScroll);

            DateTime scrollKey = date.Date;
            eventScroll.Loaded += (_, _) =>
            {
                if (_dayScrollOffsets.TryGetValue(scrollKey, out double offset))
                    eventScroll.ScrollToVerticalOffset(offset);
            };
            eventScroll.ScrollChanged += (_, _) =>
            {
                if (eventScroll.IsLoaded)
                    _dayScrollOffsets[scrollKey] = eventScroll.VerticalOffset;
            };

            // The ring lives on its own layer rather than on the cell's BorderThickness, because
            // that thickness is the GRID LINE (0,0,1,1, suppressed on the last column and row) -
            // reusing it for the today marker would blow a hole in the grid.
            var ring = new Border
            {
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            ring.Themed(Border.BorderBrushProperty, "PrimaryBrush");
            _rings[date.Date] = ring;

            var layers = new Grid();
            layers.Children.Add(content);
            var hoverGrain = new Border
            {
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            hoverGrain.SetResourceReference(Border.BackgroundProperty, "GrainTileBrush");
            hoverGrain.SetResourceReference(OpacityProperty, "GrainOpacity");
            layers.Children.Add(hoverGrain);
            layers.Children.Add(ring);
            cell.Child = layers;

            string restBrush = !inMonth
                ? "CalendarOutsideMonthBrush"
                : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                    ? "CalendarWeekendBrush"
                    : displayColumn % 2 == 0 ? "CalendarEvenColumnBrush" : "CalendarOddColumnBrush";
            _cells[date.Date] = (cell, dayNum, inMonth, isToday, restBrush);

            void Rest() => ApplyRest(cell, dayNum, date.Date, inMonth, isToday, restBrush);
            Rest();
            cell.MouseEnter += (_, _) =>
            {
                ApplyPointerState(cell, dayNum, inMonth, "RowHoverBrush");
                hoverGrain.Visibility = Visibility.Visible;
            };
            cell.MouseLeave += (_, _) =>
            {
                hoverGrain.Visibility = Visibility.Collapsed;
                Rest();
            };

            // Clicking a day opens that day's agenda in the sidebar - viewing first, editing
            // behind an Edit action. The context menu below still offers the explicit create.
            // (2026-07-30)
            cell.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                // Press state: one tier brighter than hover, so the click is acknowledged before
                // the sidebar slides out. MouseLeave restores it if the pointer moves away first.
                ApplyPointerState(cell, dayNum, inMonth, "SurfaceBrush");
                DaySelected?.Invoke(date.Date);
            };
            cell.MouseLeftButtonUp += (_, _) =>
                ApplyPointerState(cell, dayNum, inMonth, "RowHoverBrush");
            // Right-click is the explicit create, for anyone who wants to skip the agenda.
            cell.ContextMenu = CalendarChrome.DayMenu(date.Date.AddHours(9), d => SlotSelected?.Invoke(d));

            return cell;
        }

        /// <summary>
        /// Month translates the shared four-step density scale into progressively richer event
        /// marks: stripe, title, start time + title, then a taller two-line full time range/title
        /// card. This gives the
        /// rail control a visible purpose without pretending a month grid has hourly subdivisions.
        /// Tooltips retain the full title/time/location at every level.
        /// </summary>
        private Border BuildMonthChip(CalendarEvent ev)
        {
            Guid shrinkKey = ev.SeriesKey;
            bool shrunk = _shrunkAppointments.Contains(shrinkKey);
            int detail = shrunk ? 0 : CalendarChrome.Density;
            var chip = CalendarChrome.Chip(ev, OnChipClick,
                showTime: detail == 2, showEndTime: false);

            var menu = chip.ContextMenu ?? new ContextMenu();
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var add = new MenuItem
            {
                Header = Services.LocaleManager.Loc("Str_Ctx_AddAppointment"),
                Icon = CalendarChrome.MenuGlyph(0xE710),
                InputGestureText = "N",
            };
            add.Click += (_, _) => SlotSelected?.Invoke(ev.Start.Date.AddHours(9));
            menu.Items.Add(add);
            var sizeItem = new MenuItem
            {
                Header = Services.LocaleManager.Loc(shrunk ? "Str_Ctx_Expand" : "Str_Ctx_Shrink"),
                // Chevron-down expands detail; chevron-up collapses it. These are known MDL2
                // glyphs, unlike the old codepoints which rendered as a stray letter on Windows.
                Icon = CalendarChrome.MenuGlyph(shrunk ? 0xE70D : 0xE70E),
                InputGestureText = "Ctrl+Shift+M",
            };
            void ToggleSize()
            {
                if (shrunk) _shrunkAppointments.Remove(shrinkKey);
                else _shrunkAppointments.Add(shrinkKey);
                Settings.Set(ShrunkAppointmentsSetting,
                    string.Join(",", _shrunkAppointments.OrderBy(x => x)));
                Rebuild();
            }
            sizeItem.Click += (_, _) => ToggleSize();
            menu.Items.Add(sizeItem);
            menu.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.M ||
                    (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                    (ModifierKeys.Control | ModifierKeys.Shift)) return;
                e.Handled = true;
                menu.IsOpen = false;
                ToggleSize();
            };
            chip.ContextMenu = menu;
            chip.KeyDown += (_, e) =>
            {
                if (e.Key != Key.M ||
                    (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                    (ModifierKeys.Control | ModifierKeys.Shift)) return;
                e.Handled = true;
                ToggleSize();
            };

            if (detail == 0)
            {
                chip.Child = null;
                chip.Height = 5;
                chip.MinHeight = 5;
                chip.Padding = new Thickness(0);
                chip.Margin = new Thickness(0, 2, 0, 2);
            }
            else if (detail == 3)
            {
                // The previous top two settings differed only by a few characters at the start
                // of the same tiny line, so in practice month view appeared to have two density
                // levels. Give the richest setting an actual second line and a little height.
                Brush foreground = (chip.Child as TextBlock)?.Foreground ?? Brushes.White;
                string title = string.IsNullOrWhiteSpace(ev.Title)
                    ? Services.LocaleManager.Loc("Str_Cal_NoTitle") : ev.Title;
                string time = ev.AllDay
                    ? Services.LocaleManager.Loc("Str_Cal_AllDay")
                    : ev.Start.ToString("h:mm") + "-" + ev.End.ToString("h:mm");

                var lines = new StackPanel { Margin = new Thickness(0) };
                lines.Children.Add(new TextBlock
                {
                    Text = time,
                    FontSize = 8.5,
                    Foreground = foreground,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                lines.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 10,
                    Foreground = foreground,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                chip.Child = lines;
                chip.Padding = new Thickness(4, 1, 4, 2);
            }

            return chip;
        }

        // date -> the cell and what it is, so a selection change can repaint two cells instead of
        // rebuilding all 42 (each of which re-queries the store for its events).
        private readonly System.Collections.Generic.Dictionary<DateTime, (Border Cell, TextBlock DayNum, bool InMonth, bool IsToday, string RestBrush)> _cells = [];
        private readonly System.Collections.Generic.Dictionary<DateTime, Border> _rings = [];

        /// <summary>
        /// The cell's resting appearance for the current selection. Called on build, on mouse
        /// leave, and when the selected day moves.
        /// </summary>
        private void ApplyRest(Border cell, TextBlock dayNum, DateTime date, bool inMonth, bool isToday, string restBrush)
        {
            bool selected = _selectedDay == date;
            // Today keeps the fill only while nothing is selected; otherwise the fill belongs to
            // the day being edited and today wears the ring instead.
            bool todayFilled = isToday && _selectedDay == null;

            if (selected || todayFilled)
            {
                cell.SetResourceReference(Border.BackgroundProperty, "SelectionBg");
                dayNum.SetResourceReference(TextBlock.ForegroundProperty, "SelectionFg");
            }
            else
            {
                if (inMonth)
                    cell.SetResourceReference(Border.BackgroundProperty, restBrush);
                else
                    cell.Background = Brushes.Transparent;
                dayNum.SetResourceReference(TextBlock.ForegroundProperty, inMonth ? "TextBrush" : "DimTextBrush");
            }

            if (_rings.TryGetValue(date, out var ring))
                ring.Visibility = isToday && !todayFilled && !selected
                                  ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void ApplyPointerState(Border cell, TextBlock dayNum, bool inMonth, string background)
        {
            cell.SetResourceReference(Border.BackgroundProperty, background);
            dayNum.SetResourceReference(TextBlock.ForegroundProperty, inMonth ? "TextBrush" : "DimTextBrush");
        }

        /// <summary>Re-apply one day's resting appearance, if that day is on screen.</summary>
        private void RepaintDay(DateTime? date)
        {
            if (date is not DateTime d) return;
            if (_cells.TryGetValue(d.Date, out var e))
                ApplyRest(e.Cell, e.DayNum, d.Date, e.InMonth, e.IsToday, e.RestBrush);
        }

        private void OnChipClick(CalendarEvent ev) => EventSelected?.Invoke(ev);

        /// <summary>
        /// Drag a chip to another day (2026-07-31). Same shape as TimeGridView.WireDrag:
        /// the chip rides a RenderTransform, a press that never travels stays a click, and
        /// everything is read BEFORE ReleaseMouseCapture - that release fires LostMouseCapture,
        /// which resets the drag state (the lesson the week grid taught the same day). The drop
        /// cell is plain arithmetic: uniform star rows and columns over StartOfGrid.
        /// The chip's z-order is per-panel, so the CELL is lifted while in flight - lifting only
        /// the chip would slide it underneath every later-built neighbor cell.
        /// </summary>
        private void WireDrag(Border chip, CalendarEvent ev, Border cell)
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
                Panel.SetZIndex(cell, 0);
            }

            chip.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
            {
                start = e.GetPosition(CalGrid);
                dragging = false;
                chip.CaptureMouse();
            }), handledEventsToo: true);

            chip.MouseMove += (_, e) =>
            {
                if (!chip.IsMouseCaptured) return;
                var p = e.GetPosition(CalGrid);
                double dx = p.X - start.X, dy = p.Y - start.Y;
                if (!dragging &&
                    (Math.Abs(dx) >= SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(dy) >= SystemParameters.MinimumVerticalDragDistance))
                {
                    dragging = true;
                    chip.Opacity = 0.7;
                    Panel.SetZIndex(cell, 99);
                }
                if (dragging) { move.X = dx; move.Y = dy; }
            };

            chip.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, e) =>
            {
                if (!chip.IsMouseCaptured) return;
                bool wasDragging = dragging;
                var p = e.GetPosition(CalGrid);
                chip.ReleaseMouseCapture();          // fires LostMouseCapture -> Reset()
                if (!wasDragging) return;

                if (CalGrid.ActualWidth < 1 || CalGrid.ActualHeight < 1) return;
                int rows = Math.Max(1, CalGrid.RowDefinitions.Count);
                int c = (int)Math.Max(0, Math.Min(6, p.X / (CalGrid.ActualWidth / 7)));
                int r = (int)Math.Max(0, Math.Min(rows - 1, p.Y / (CalGrid.ActualHeight / rows)));
                var target = (_visibleWeeks > 0 ? _rollingStart : StartOfGrid(_anchor))
                    .AddDays(r * 7 + c).Date;

                // Month moves the DATE and keeps the clock; an all-day event lands on the date
                // itself and the controller keeps its span.
                EventDropped?.Invoke(ev, target + (ev.AllDay ? TimeSpan.Zero : ev.Start.TimeOfDay));
            }), handledEventsToo: true);

            chip.LostMouseCapture += (_, _) => Reset();
        }
    }
}
