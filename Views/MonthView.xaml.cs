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
        public event Action<DateTime>? SlotSelected;

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
                    // look correct, because cells have no left/top border to double up (Steve,
                    // 2026-07-29). Drop the line on the outer edges and let the card's border be it.
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

            // Days outside the current month sit back; today gets a neutral raised fill.
            //
            // In-month days carry NO fill of their own. They used to paint PaneBrush, which is the
            // same colour as the card underneath - so it looked identical while quietly covering
            // the card's film grain, and only the semi-transparent out-of-month cells (RowAltBrush
            // is #14FFFFFF) showed any texture. Transparent, NOT null: a Border with a null
            // Background receives no mouse events, and the cell has to stay clickable.
            // Today gets a neutral raised fill rather than the accent-tinted row brush: the event
            // chips are accent-tinted too and would wash out against it. The red day number is
            // what marks today.
            string? restKey = isToday ? "SurfaceBrush" : inMonth ? null : "RowAltBrush";
            void Rest()
            {
                if (restKey == null) cell.Background = Brushes.Transparent;
                else cell.SetResourceReference(Border.BackgroundProperty, restKey);
            }
            Rest();
            cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
            cell.MouseLeave += (_, _) => Rest();

            // Clicking empty space in a day starts a new appointment at 9am on that date.
            cell.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                SlotSelected?.Invoke(date.Date.AddHours(9));
            };

            var sp = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };

            var dayNum = CalendarChrome.Text(
                date.Day.ToString(),
                isToday ? "PrimaryBrush" : inMonth ? "TextBrush" : "DimTextBrush",
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
                sp.Children.Add(CalendarChrome.Chip(ev, OnChipClick));
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

            cell.Child = sp;
            return cell;
        }

        private void OnChipClick(CalendarEvent ev) => EventSelected?.Invoke(ev);
    }
}
