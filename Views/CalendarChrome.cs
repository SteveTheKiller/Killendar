using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Models;

namespace Killendar.Views
{
    /// <summary>
    /// Shared bits for the code-built calendar views.
    ///
    /// The one rule that matters here: every brush on a code-built element goes on with
    /// SetResourceReference, never a cached SolidColorBrush. A snapshot brush does not follow
    /// a theme switch, which is exactly why the pre-rewrite views hardcoded dark green and
    /// could never be themed - a net48 gotcha.
    /// </summary>
    internal static class CalendarChrome
    {
        // ---- time-grid density ----
        //
        // One scale drives BOTH the hour height and the number of gridlines inside an hour, so a
        // denser grid is also a taller one (2026-07-30). Level 0 is the original 48px hour
        // with no interior lines; each step up adds a line and more room to fit it in.
        //
        // Subdivisions are lines-per-hour: 1 = the hour line only, 2 = a :30 line, 3 = :20 and :40,
        // 4 = quarter hours. HourHeight rises with it because a 48px hour cut into quarters gives
        // 12px rows, which is thinner than the text that has to sit in them.
        private static readonly (double Height, int Subdivisions)[] DensitySteps =
        [
            (48,  1),   // 0 - hour lines only
            (64,  2),   // 1 - half hours
            (84,  3),   // 2 - thirds
            (108, 4),   // 3 - quarter hours
        ];

        internal const int MaxDensity = 3;

        /// <summary>Week view shows Monday to Friday when set (2026-07-31). Owned by the
        /// shell like Density (WorkWeek.cs): persisted there, read here by WeekView on rebuild.</summary>
        internal static bool WorkWeek;

        /// <summary>Flips the work-week setting. Set by the shell (WorkWeek.cs), invoked by the
        /// toggle WeekView builds into its own header - the view cannot reach Settings or the
        /// status line, and this one hook is cheaper than routing an event through the
        /// controller for a single button.</summary>
        internal static Action? WorkWeekToggle;

        /// <summary>Opens the series MASTER in the editor - the chip context menu's "Edit the
        /// series" (2026-07-31, Outlook's reschedule-the-series: a drag or an edit only
        /// ever touches one date, so the whole series needs its own door). Set by the
        /// controller, which owns the store and the editor; same shape as WorkWeekToggle.</summary>
        internal static Action<CalendarEvent>? EditSeriesRequested;

        private static int _density;

        /// <summary>Current density step, 0 to MaxDensity. Clamped on set.</summary>
        internal static int Density
        {
            get => _density;
            set => _density = value < 0 ? 0 : value > MaxDensity ? MaxDensity : value;
        }

        /// <summary>Pixels per hour at the current density. Was a const 48.</summary>
        internal static double HourHeight => DensitySteps[_density].Height;

        /// <summary>Gridlines drawn per hour at the current density, including the hour line.</summary>
        internal static int Subdivisions => DensitySteps[_density].Subdivisions;

        /// <summary>
        /// Minutes a click snaps to, following the visible subdivision - so the grid never shows a
        /// line you cannot land on, and never lands you somewhere it did not draw.
        /// </summary>
        internal static int SnapMinutes => 60 / DensitySteps[_density].Subdivisions;

        /// <summary>Bind a dependency property to a theme resource key so it repaints on theme change.</summary>
        internal static T Themed<T>(this T el, DependencyProperty prop, string key) where T : FrameworkElement
        {
            el.SetResourceReference(prop, key);
            return el;
        }

        internal static TextBlock Text(string text, string brushKey, double size,
                                       FontWeight? weight = null, string? fontFamily = null)
        {
            var tb = new TextBlock { Text = text, FontSize = size };
            tb.Themed(TextBlock.ForegroundProperty, brushKey);
            if (weight.HasValue) tb.FontWeight = weight.Value;
            if (fontFamily != null) tb.FontFamily = new FontFamily(fontFamily);
            return tb;
        }

        /// <summary>
        /// One appointment chip. The accent lives on a left edge bar rather than a filled
        /// background, so it reads the same against all six palettes instead of glowing on Light.
        ///
        /// A categorized event is the exception: it is filled with its first category's color,
        /// which is the whole point of categories, and its title switches to black or white by
        /// luminance so it reads on a pale one. Uncategorized events are untouched by that and
        /// keep the themed look described above.
        /// </summary>
        internal static Border Chip(CalendarEvent ev, Action<CalendarEvent> onClick,
                                    bool compact = true, bool showTime = false, bool wrap = false,
                                    bool showEndTime = false)
        {
            string? category = Services.CategoryManager.PrimaryOf(ev);
            var chip = new Border
            {
                CornerRadius    = Services.ThemeManager.Current == Services.Theme.SE98
                                    ? new CornerRadius(0) : new CornerRadius(2),
                Margin          = new Thickness(0, 1, 0, 1),
                Padding         = compact ? new Thickness(4, 1, 4, 1) : new Thickness(6, 3, 6, 3),
                BorderThickness = new Thickness(2, 0, 0, 0),
                Cursor          = Cursors.Hand,
                Focusable       = true,
                Tag             = ev,
                ToolTip         = string.IsNullOrWhiteSpace(ev.Location)
                                    ? ev.Title + "\n" + ev.TimeLabel
                                    : ev.Title + "\n" + ev.TimeLabel + "\n" + ev.Location
            };
            if (category != null)
            {
                var fill = Services.CategoryManager.BrushOf(category);
                chip.Background  = fill;
                chip.BorderBrush = fill;
                // Hover cannot swap in a theme brush here or the category color would drop out
                // for as long as the pointer is over the chip. Dimming keeps the color and still
                // gives the same "this is live" feedback.
                chip.MouseEnter += (_, _) => chip.Opacity = 0.82;
                chip.MouseLeave += (_, _) => chip.Opacity = 1.0;
            }
            else
            {
                // ChipBrush, not RowSelectedBrush: every theme gives untagged chips a color of
                // their OWN that no accent overlay repoints, because the row/selection brushes
                // collided with the day fills - on Black the chip was one shade off SelectionBg
                // and vanished inside the selected day (2026-07-31). Hover dims like the
                // categorized chips instead of swapping to RowHoverBrush, which would make the
                // chip match the hovered cell it is sitting on.
                chip.Themed(Border.BackgroundProperty, "ChipBrush");
                chip.Themed(Border.BorderBrushProperty, "PrimaryBrush");

                chip.MouseEnter += (_, _) => chip.Opacity = 0.82;
                chip.MouseLeave += (_, _) => chip.Opacity = 1.0;
            }
            // Click fires on RELEASE, and only when the pointer has not moved a drag's worth:
            // the grid views move chips by dragging (2026-07-31), and firing on press
            // opened the day agenda at the start of every drag. A view's drag wiring registers
            // with handledEventsToo, so Handled here stops the cell underneath, not the drag.
            //
            // Measured against the WINDOW (GetPosition(null)), never against the chip: during a
            // drag the chip rides the pointer, so chip-relative distance stays near zero and a
            // finished drag read as a click - which reopened the day agenda on the OLD day the
            // moment a month drop landed.
            Point pressedAt = default;
            bool pressed = false;
            chip.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;   // do not let the day cell underneath also fire
                pressed = true;
                pressedAt = e.GetPosition(null);
            };
            chip.MouseLeftButtonUp += (_, e) =>
            {
                if (!pressed) return;
                pressed = false;
                e.Handled = true;
                var p = e.GetPosition(null);
                if (Math.Abs(p.X - pressedAt.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(p.Y - pressedAt.Y) < SystemParameters.MinimumVerticalDragDistance)
                    onClick(ev);
            };

            chip.MouseRightButtonDown += (_, _) => chip.Focus();
            chip.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                onClick(ev);
            };

            // Every appointment menu starts with the ordinary open action. One date of a series
            // also carries "Edit the series": dragging or editing
            // a chip only ever moves that date, so rescheduling the SERIES gets its own door
            // (2026-07-31). The chip's own menu wins over the day cell's add-appointment
            // menu because it is the closer one.
            var menu = new ContextMenu();
            var open = new MenuItem
            {
                Header = Services.LocaleManager.Loc("Str_Ctx_OpenAppointment"),
                Icon = MenuGlyph(0xE8E5),
                InputGestureText = "Enter",
            };
            open.Click += (_, _) => onClick(ev);
            menu.Items.Add(open);

            if (ev.SeriesId != null)
            {
                var edit = new MenuItem
                {
                    Header = Services.LocaleManager.Loc("Str_Ctx_EditSeries"),
                    Icon   = MenuGlyph(0xE70F),
                };
                edit.Click += (_, _) => EditSeriesRequested?.Invoke(ev);
                menu.Items.Add(edit);
            }
            chip.ContextMenu = menu;

            // wrap: the whole title, however long, instead of one trimmed line. The sidebar day
            // agenda asks for this at higher densities; the grid views never do - a wrapping
            // chip in a month cell would push its neighbors out of the cell.
            var line = new TextBlock
            {
                TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                FontSize     = compact ? 10 : 11.5
            };
            if (category != null)
                line.Foreground = Services.CategoryManager.ForegroundFor(
                    Services.CategoryManager.ColorOf(category));
            else
                line.Themed(TextBlock.ForegroundProperty, "TextBrush");

            if (showTime && !ev.AllDay)
            {
                string time = showEndTime
                    ? ev.Start.ToString("h:mm") + "-" + ev.End.ToString("h:mm")
                    : ev.Start.ToString("h:mm");
                line.Inlines.Add(new Run(time + "  "));
            }

            line.Inlines.Add(new Run(string.IsNullOrWhiteSpace(ev.Title) ? Services.LocaleManager.Loc("Str_Cal_NoTitle") : ev.Title));
            chip.Child = line;
            return chip;
        }

        /// <summary>
        /// Right-click menu for a day cell or an hour slot. Shared so month, week and day all
        /// offer the same thing, and so the wording lives in one place.
        /// <paramref name="at"/> is the moment a new appointment would start.
        /// </summary>
        internal static ContextMenu DayMenu(DateTime at, Action<DateTime> onAdd)
        {
            var menu = new ContextMenu();
            var add = new MenuItem
            {
                Header = Services.LocaleManager.Loc("Str_Ctx_AddAppointment"),
                // Icon left, shortcut right - the family's menu shape. E710 is Add, the same glyph
                // the New button on the toolbar carries, so the menu names the button.
                Icon = MenuGlyph(0xE710),
                // N, matching the "N" binding in ShortcutsOverlay.cs KsAll. Killendar prefers a
                // bare key over a Ctrl combo; Ctrl+N is only the compatibility alias.
                InputGestureText = "N",
            };
            add.Click += (_, _) => onAdd(at);
            menu.Items.Add(add);
            return menu;
        }

        /// <summary>
        /// A Segoe MDL2 glyph ready to hand to MenuItem.Icon. A TextBlock rather than a bare
        /// string so the glyph carries its own font and its own muted brush - and so the menu
        /// template's highlight, which recolors by inheritance, still reaches it.
        /// Built from the codepoint, never a literal private-use character: those do not survive
        /// tooling (the same trap that corrupted Sidebar.cs's chevrons twice).
        /// </summary>
        internal static TextBlock MenuGlyph(int codepoint)
        {
            var tb = new TextBlock
            {
                Text = char.ConvertFromUtf32(codepoint),
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return tb;
        }

        /// <summary>Hour label for the time gutter, e.g. "9 AM".</summary>
        internal static string HourLabel(int hour) => DateTime.Today.AddHours(hour).ToString("h tt");
    }
}
