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
    internal abstract partial class TimeGridView
    {
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
            // and Day did not match Month. (2026-07-30)
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
            // to be findable after the pointer has moved away. (2026-07-30)
            var selected = new Border
            {
                Height = slot,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                // All four edges, not just top and bottom (2026-07-31): with open sides
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
            var placements = CalendarLayout.PlaceTimedDay(_store!.GetOnDay(date), date);
            foreach (var placement in placements)
            {
                var ev = placement.Event;
                double top = (placement.VisibleStart - dayStart).TotalHours * CalendarChrome.HourHeight;
                double height = Math.Max(16,
                    (placement.VisibleEnd - placement.VisibleStart).TotalHours * CalendarChrome.HourHeight - 2);
                int myLanes = placement.LaneCount;

                // The time prefix only in Day view with no overlap: in a time grid the chip's
                // POSITION already says the time, and in Week's seventh-width columns the prefix
                // was eating the title down to "9:00 Morni" (2026-07-31). The tooltip
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

                int lane = placement.Lane;
                // Width is a fraction of the column, resolved on layout since the column is star-sized.
                canvas.SizeChanged += (_, _) => PositionChip(chip, canvas, lane, myLanes);
                PositionChip(chip, canvas, lane, myLanes);

                WireDrag(chip, ev, top, canvas);
                canvas.Children.Add(chip);
            }
        }

        /// <summary>
        /// Drag a chip to another slot or another day (2026-07-31). The chip follows the
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
                    // own column edge (2026-07-31). While in flight the CANVAS is unclipped
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
                // committed-nothing (2026-07-31).
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
                // half-hour appointments unplaceable at density 0 (2026-07-31). A denser
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
}
