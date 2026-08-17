using System;
using System.Collections.Generic;
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
    internal abstract partial class TimeGridView
    {
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
            _todayDow = null;
            _todayNum = null;
            _headerRow.Children.Clear();
            _bodyGrid.Children.Clear();
            _bodyGrid.RowDefinitions.Clear();
            _allDayStrip.Children.Clear();
            _allDayStrip.ColumnDefinitions.Clear();
            _allDayStrip.RowDefinitions.Clear();

            ApplyColumns(_headerRow);
            ApplyColumns(_bodyGrid);

            var start = RangeStart;
            var today = DateTime.Today;

            // Day already has its full date in the large toolbar label and can only ever show
            // that one column. Repeating "SUN 16" below it wastes a whole header band.
            _headerDefinition.Height = Days == 1 ? new GridLength(0) : new GridLength(30);
            _headerRow.Visibility = Days == 1 ? Visibility.Collapsed : Visibility.Visible;

            // The work-week toggle sits in the header's gutter corner, left of the first day -
            // where the thing it changes is (2026-07-31; it was on the rail first, which
            // put it a whole window away from the columns it drops). Rebuilt with the header, so
            // its lit state always matches the columns beside it.
            if (OffersWorkWeekToggle)
            {
                // A labeled button that says 5-Day, not a bare glyph (2026-07-31). The STYLE is
                // the state: off it is the gray SurfaceButton, the Cancel treatment; on it is
                // the accent OutlineButton, the OK treatment - which also means the moment it is
                // clicked it shows solid under the pointer (OutlineButton's hover fill) and
                // settles to the accent outline when the mouse leaves. The header rebuilds on
                // every toggle, so the style swap is just picking the right key at build.
                var ww = new Button
                {
                    Content = LocaleManager.Loc("Str_Btn_WorkWeek"),
                    FontSize = 10,
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(1, 3, 1, 3),
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
                    "DimTextBrush", 10);
                dow.VerticalAlignment = VerticalAlignment.Center;
                dow.Margin = new Thickness(0, 0, 6, 0);
                // SelectionFg on BOTH the weekday name and the number, matching MonthView. The name
                // used to be PrimaryBrush while the number was SelectionFg, so "THU 30" was printed
                // in two different colors on the one header. (2026-07-30)
                var num = CalendarChrome.Text(date.Day.ToString(),
                    isToday ? "PrimaryBrush" : "TextBrush", 13,
                    isToday ? FontWeights.Bold : (FontWeight?)null);
                num.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(dow);
                sp.Children.Add(num);

                // The header is a quiet axis, matching Month: no selected fill and no outline.
                // Today is carried by the bold accent date number and by the column below.
                var head = new Border { Child = sp, Padding = new Thickness(0, 2, 0, 2) };

                // The header is the "whole day" surface a time grid has, so clicking it opens the
                // day's agenda in the sidebar - the empty slots below stay the explicit create.
                // Transparent, not null: a null Background gets no mouse events. (2026-07-30)
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
                    _todayDow = dow;
                    _todayNum = num;
                }

                Grid.SetColumn(head, i + 1);
                _headerRow.Children.Add(head);
            }

            // ---- all-day strip ----
            ApplyColumns(_allDayStrip);
            var allDay = CalendarLayout.PlaceAllDayRange(
                _store.GetInRange(start, start.AddDays(Days)), start, Days);
            if (allDay.Count > 0)
            {
                _allDayHost.Visibility = Visibility.Visible;
                for (int row = 0; row <= allDay.Max(x => x.Row); row++)
                    _allDayStrip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                foreach (var placement in allDay)
                {
                    var chip = CalendarChrome.Chip(
                        placement.Event, e => EventSelected?.Invoke(e), compact: false);
                    chip.Margin = new Thickness(2, 1, 2, 1);
                    Grid.SetColumn(chip, placement.StartColumn + 1);
                    Grid.SetColumnSpan(chip, placement.ColumnSpan);
                    Grid.SetRow(chip, placement.Row);
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
                Canvas.SetRight(lbl, 4);
                lbl.Width = 40;
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
    }
}
