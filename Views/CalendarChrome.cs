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
        // denser grid is also a taller one (Steve, 2026-07-30). Level 0 is the original 48px hour
        // with no interior lines; each step up adds a line and more room to fit it in.
        //
        // Subdivisions are lines-per-hour: 1 = the hour line only, 2 = a :30 line, 3 = :20 and :40,
        // 4 = quarter hours. HourHeight rises with it because a 48px hour cut into quarters gives
        // 12px rows, which is thinner than the text that has to sit in them.
        private static readonly (double Height, int Subdivisions)[] DensitySteps =
        {
            (48,  1),   // 0 - hour lines only
            (64,  2),   // 1 - half hours
            (84,  3),   // 2 - thirds
            (108, 4),   // 3 - quarter hours
        };

        internal const int MaxDensity = 3;

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
                                    bool compact = true, bool showTime = false)
        {
            string? category = Services.CategoryManager.PrimaryOf(ev);
            var chip = new Border
            {
                CornerRadius    = new CornerRadius(2),
                Margin          = new Thickness(0, 1, 0, 1),
                Padding         = compact ? new Thickness(4, 1, 4, 1) : new Thickness(6, 3, 6, 3),
                BorderThickness = new Thickness(2, 0, 0, 0),
                Cursor          = Cursors.Hand,
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
                chip.Themed(Border.BackgroundProperty, "RowSelectedBrush");
                chip.Themed(Border.BorderBrushProperty, "PrimaryBrush");

                chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
                chip.MouseLeave += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "RowSelectedBrush");
            }
            chip.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;   // do not let the day cell underneath also fire
                onClick(ev);
            };

            var line = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize     = compact ? 10 : 11.5
            };
            if (category != null)
                line.Foreground = Services.CategoryManager.ForegroundFor(
                    Services.CategoryManager.ColorOf(category));
            else
                line.Themed(TextBlock.ForegroundProperty, "TextBrush");

            if (showTime && !ev.AllDay)
                line.Inlines.Add(new Run(ev.Start.ToString("h:mm") + "  "));

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
        /// template's highlight, which recolours by inheritance, still reaches it.
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
            tb.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
            return tb;
        }

        /// <summary>Hour label for the time gutter, e.g. "9 AM".</summary>
        internal static string HourLabel(int hour) => DateTime.Today.AddHours(hour).ToString("h tt");
    }
}
