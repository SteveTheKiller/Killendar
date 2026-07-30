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
    /// could never be themed (net48 gotcha, see code/CLAUDE.md).
    /// </summary>
    internal static class CalendarChrome
    {
        internal const double HourHeight = 48;

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
        /// </summary>
        internal static Border Chip(CalendarEvent ev, Action<CalendarEvent> onClick,
                                    bool compact = true, bool showTime = false)
        {
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
            chip.Themed(Border.BackgroundProperty, "RowSelectedBrush");
            chip.Themed(Border.BorderBrushProperty, "PrimaryBrush");

            chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
            chip.MouseLeave += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "RowSelectedBrush");
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
            line.Themed(TextBlock.ForegroundProperty, "TextBrush");

            if (showTime && !ev.AllDay)
                line.Inlines.Add(new Run(ev.Start.ToString("h:mm") + "  "));

            line.Inlines.Add(new Run(string.IsNullOrWhiteSpace(ev.Title) ? MainWindow.LocStatic("Str_Cal_NoTitle") : ev.Title));
            chip.Child = line;
            return chip;
        }

        /// <summary>Hour label for the time gutter, e.g. "9 AM".</summary>
        internal static string HourLabel(int hour) => DateTime.Today.AddHours(hour).ToString("h tt");
    }
}
