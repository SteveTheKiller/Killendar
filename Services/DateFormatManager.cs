using System;
using System.Globalization;

namespace Killendar.Services
{
    /// <summary>
    /// Which date format the appointment editor writes and reads. This exists because the two were
    /// allowed to disagree: the localized hint said jjjj-mm-tt in German while the parser only took
    /// ISO, so a user following the hint got an error pointing at a format the hint never showed.
    /// One source of truth for the pattern, the hint, and the parse.
    /// </summary>
    public enum DateStyle { FollowWindows, Iso, US, EU }

    public static class DateFormatManager
    {
        public static Func<string, string?> GetSetting { get; set; } = _ => null;
        public static Action<string, string> SetSetting { get; set; } = (_, _) => { };

        // ISO by default rather than FollowWindows: it sorts, it is unambiguous, and it is what the
        // .ics files this app reads and writes use.
        private static DateStyle _current = DateStyle.Iso;
        public static DateStyle Current => _current;

        /// <summary>Fired after the style changes, so open editors can relabel and reformat.</summary>
        public static event Action? Changed;

        public static void Initialize()
        {
            _current = Enum.TryParse<DateStyle>(GetSetting("DateStyle"), out var s) ? s : _current;
        }

        public static void Apply(DateStyle style)
        {
            _current = style;
            SetSetting("DateStyle", style.ToString());
            Changed?.Invoke();
        }

        /// <summary>The .NET format string for the current style.</summary>
        public static string Pattern => _current switch
        {
            DateStyle.Iso => "yyyy-MM-dd",
            DateStyle.US  => "M/d/yyyy",
            DateStyle.EU  => "dd/MM/yyyy",
            _             => CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern,
        };

        /// <summary>What to show as the field hint. Derived from the pattern rather than translated,
        /// so it can never drift from what the parser actually accepts.</summary>
        public static string Hint => Pattern
            .Replace("yyyy", "yyyy").Replace("MM", "mm").Replace("M", "m")
            .Replace("dd", "dd").Replace("d", "d")
            .ToLowerInvariant()
            .Replace("yyyy", "yyyy");

        /// <summary>Format a date for display in the editor.</summary>
        public static string Format(DateTime d) => d.ToString(Pattern, CultureInfo.CurrentCulture);

        /// <summary>
        /// Parse what the user typed. The selected pattern wins, then ISO as a permanent fallback
        /// (so an .ics-shaped date always works), then whatever the OS culture accepts. Being
        /// generous here costs nothing and saves the user from the format they were not shown.
        /// </summary>
        public static bool TryParse(string raw, out DateTime date)
        {
            date = default;
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return false;

            var formats = new[] { Pattern, "yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "dd/MM/yyyy", "d.M.yyyy", "dd.MM.yyyy" };
            if (DateTime.TryParseExact(raw, formats, CultureInfo.CurrentCulture,
                                       DateTimeStyles.None, out date)) return true;

            return DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
        }
    }
}
