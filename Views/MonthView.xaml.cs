using System;
using System.Globalization;
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

        public event Action<CalendarEvent>? EventSelected;
        public event Action<DateTime>? DaySelected;
        public event Action<DateTime>? SlotSelected;

        /// <summary>A chip dragged to another day (Steve, 2026-07-31). Month keeps the clock and
        /// moves the DATE; the controller commits - an occurrence as an override, never the series.</summary>
        public event Action<CalendarEvent, DateTime>? EventDropped;

        /// <summary>
        /// Never raised - a month cell is a day, with no hour grid to make denser. Declared with
        /// empty accessors rather than as a field: a field-like event that nothing raises is
        /// CS0067, and the interface still has to be satisfied.
        /// </summary>
        public event Action<int>? DensityStepped { add { } remove { } }

        public MonthView() => InitializeComponent();

        public void Initialize(EventStore store)
        {
            _store = store;
            Rebuild();
        }

        public DateTime Anchor
        {
            get => _anchor;
            set { _anchor = value; Rebuild(); }
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
            // this is what put two solid cells on screen at once. (Steve, 2026-07-30.)
            if (was == null || v == null) RepaintDay(DateTime.Today);
        }

        public string PeriodLabel => _anchor.ToString("MMMM yyyy");

        public DateTime Step(DateTime from, int direction) => from.AddMonths(direction);

        public void Refresh() => Rebuild();

        /// <summary>First day of the week for the current culture, so this is not hardcoded to Sunday.</summary>
        private static DayOfWeek FirstDay => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

        private static DateTime StartOfGrid(DateTime anchor)
        {
            var first = new DateTime(anchor.Year, anchor.Month, 1);
            int shift = ((int)first.DayOfWeek - (int)FirstDay + 7) % 7;
            return first.AddDays(-shift);
        }

        private void Rebuild()
        {
            if (_store == null) return;

            BuildWeekdayHeader();

            // The maps point at Borders that are about to be discarded. Stale entries would make
            // RepaintDay colour a cell that is no longer in the tree.
            _cells.Clear();
            _rings.Clear();

            CalGrid.Children.Clear();
            CalGrid.RowDefinitions.Clear();
            CalGrid.ColumnDefinitions.Clear();
            for (int c = 0; c < 7; c++)
                CalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var start = StartOfGrid(_anchor);
            var lastOfMonth = new DateTime(_anchor.Year, _anchor.Month, 1).AddMonths(1).AddDays(-1);

            // Only as many weeks as the month actually needs (4 to 6).
            int totalDays = (int)(lastOfMonth - start).TotalDays + 1;
            int rows = (int)Math.Ceiling(totalDays / 7.0);

            for (int r = 0; r < rows; r++)
                CalGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var today = DateTime.Today;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < 7; c++)
                {
                    var date = start.AddDays(r * 7 + c);
                    // Cells draw their own right and bottom grid lines. On the last column and the
                    // last row that line lands right against the card's own 1px border, reading as
                    // a doubled edge - one pixel off on the right and bottom while the left and top
                    // look correct, because cells have no left/top border to double up. Drop the
                    // line on the outer edges and let the card's border be it.
                    var cell = BuildDayCell(date, date.Month == _anchor.Month, date == today,
                                            lastColumn: c == 6, lastRow: r == rows - 1);
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    CalGrid.Children.Add(cell);
                }
            }
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
                Grid.SetColumn(tb, c);
                WeekdayHeader.Children.Add(tb);
            }
        }

        private Border BuildDayCell(DateTime date, bool inMonth, bool isToday,
                                    bool lastColumn = false, bool lastRow = false)
        {
            var events = _store!.GetOnDay(date);

            var cell = new Border
            {
                BorderThickness = new Thickness(0, 0, lastColumn ? 0 : 1, lastRow ? 0 : 1),
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
            // same colour as the card underneath - so it looked identical while quietly covering
            // the card's film grain, and only the semi-transparent out-of-month cells (RowAltBrush
            // is #14FFFFFF) showed any texture. Transparent, NOT null: a Border with a null
            // Background receives no mouse events, and the cell has to stay clickable.
            //
            // SelectionBg is the family's "this one is active" fill - the same brush behind a
            // selected tool, tab or menu item. Today used to be the neutral SurfaceBrush, which is
            // the same brush the PRESS state uses, so it looked permanently half-clicked.
            // (Steve, 2026-07-30.)
            _cells[date.Date] = (cell, inMonth, isToday);

            void Rest() => ApplyRest(cell, date.Date, inMonth, isToday);
            Rest();
            cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
            cell.MouseLeave += (_, _) => Rest();

            // Clicking a day opens that day's agenda in the sidebar - viewing first, editing
            // behind an Edit action. The context menu below still offers the explicit create.
            // (Steve, 2026-07-30.)
            cell.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                // Press state: one tier brighter than hover, so the click is acknowledged before
                // the sidebar slides out. MouseLeave restores it if the pointer moves away first.
                cell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                DaySelected?.Invoke(date.Date);
            };
            cell.MouseLeftButtonUp += (_, _) =>
                cell.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
            // Right-click is the explicit create, for anyone who wants to skip the agenda.
            cell.ContextMenu = CalendarChrome.DayMenu(date.Date.AddHours(9), d => SlotSelected?.Invoke(d));

            var sp = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };

            // SelectionFg on today, not PrimaryBrush. When today carries the SelectionBg fill, the
            // accent on its own selection fill is the weakest pairing on the card (#DD504B on
            // #5E1C1C) - SelectionBg and SelectionFg are a pair and are used as one. When today is
            // instead wearing the ring (because another day is selected), white still reads: the
            // cell is then unfilled and white is the ordinary text colour, with the ring and the
            // bold weight doing the marking.
            var dayNum = CalendarChrome.Text(
                date.Day.ToString(),
                isToday ? "SelectionFg" : inMonth ? "TextBrush" : "DimTextBrush",
                11,
                isToday ? FontWeights.Bold : (FontWeight?)null);
            dayNum.HorizontalAlignment = HorizontalAlignment.Right;
            dayNum.Margin = new Thickness(0, 1, 2, 3);
            sp.Children.Add(dayNum);

            const int maxChips = 3;
            int shown = 0;
            foreach (var ev in events)
            {
                if (shown >= maxChips) break;
                var chip = CalendarChrome.Chip(ev, OnChipClick);
                WireDrag(chip, ev, cell);
                sp.Children.Add(chip);
                shown++;
            }
            if (events.Count > shown)
            {
                var more = CalendarChrome.Text(
                    string.Format(Services.LocaleManager.Loc("Str_Cal_MoreCount"), events.Count - shown),
                    "MutedTextBrush", 9);
                more.Margin = new Thickness(2, 1, 0, 0);
                sp.Children.Add(more);
            }

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
            layers.Children.Add(sp);
            layers.Children.Add(ring);
            cell.Child = layers;

            ApplyRest(cell, date.Date, inMonth, isToday);
            return cell;
        }

        // date -> the cell and what it is, so a selection change can repaint two cells instead of
        // rebuilding all 42 (each of which re-queries the store for its events).
        private readonly System.Collections.Generic.Dictionary<DateTime, (Border Cell, bool InMonth, bool IsToday)> _cells = [];
        private readonly System.Collections.Generic.Dictionary<DateTime, Border> _rings = [];

        /// <summary>
        /// The cell's resting appearance for the current selection. Called on build, on mouse
        /// leave, and when the selected day moves.
        /// </summary>
        private void ApplyRest(Border cell, DateTime date, bool inMonth, bool isToday)
        {
            bool selected = _selectedDay == date;
            // Today keeps the fill only while nothing is selected; otherwise the fill belongs to
            // the day being edited and today wears the ring instead.
            bool todayFilled = isToday && _selectedDay == null;

            if (selected || todayFilled)
                cell.SetResourceReference(Border.BackgroundProperty, "SelectionBg");
            else if (!inMonth)
                cell.SetResourceReference(Border.BackgroundProperty, "RowAltBrush");
            else
                cell.Background = Brushes.Transparent;   // never null - a null Background gets no mouse events

            if (_rings.TryGetValue(date, out var ring))
                ring.Visibility = isToday && !todayFilled && !selected
                                  ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Re-apply one day's resting appearance, if that day is on screen.</summary>
        private void RepaintDay(DateTime? date)
        {
            if (date is not DateTime d) return;
            if (_cells.TryGetValue(d.Date, out var e))
                ApplyRest(e.Cell, d.Date, e.InMonth, e.IsToday);
        }

        private void OnChipClick(CalendarEvent ev) => EventSelected?.Invoke(ev);

        /// <summary>
        /// Drag a chip to another day (Steve, 2026-07-31). Same shape as TimeGridView.WireDrag:
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
                var target = StartOfGrid(_anchor).AddDays(r * 7 + c).Date;

                // Month moves the DATE and keeps the clock; an all-day event lands on the date
                // itself and the controller keeps its span.
                EventDropped?.Invoke(ev, target + (ev.AllDay ? TimeSpan.Zero : ev.Start.TimeOfDay));
            }), handledEventsToo: true);

            chip.LostMouseCapture += (_, _) => Reset();
        }
    }
}
