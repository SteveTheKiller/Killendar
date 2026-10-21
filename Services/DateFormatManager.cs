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

    /// <summary>12- or 24-hour display. Follow Windows uses the culture's own short time.</summary>
    public enum TimeStyle { FollowWindows, TwelveHour, TwentyFourHour }

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
            _time = Enum.TryParse<TimeStyle>(GetSetting("TimeStyle"), out var t) ? t : _time;
        }

        private static TimeStyle _time = TimeStyle.FollowWindows;
        public static TimeStyle CurrentTime => _time;

        public static void ApplyTime(TimeStyle style)
        {
            _time = style;
            SetSetting("TimeStyle", style.ToString());
            Changed?.Invoke();
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

        // ── Display times ────────────────────────────────────────────────────────────────
        //
        // Every time a view shows goes through here, so the clock the user chose is the clock
        // they see in chips, the time gutter, the agenda and exports alike. Interchange formats
        // (.ics, CSV) never do: they stay invariant whatever is chosen.

        /// <summary>Whether times read as 12-hour for <paramref name="culture"/>.</summary>
        public static bool Uses12Hour(CultureInfo culture) => _time switch
        {
            TimeStyle.TwelveHour     => true,
            TimeStyle.TwentyFourHour => false,
            _ => culture.DateTimeFormat.ShortTimePattern.IndexOf('h') >= 0 &&
                 !string.IsNullOrWhiteSpace(culture.DateTimeFormat.AMDesignator),
        };

        /// <summary>The culture's format with AM/PM filled in where the language has none, so a
        /// chosen 12-hour clock never prints a bare, ambiguous "1:05".</summary>
        public static DateTimeFormatInfo ClockFormat(CultureInfo culture)
        {
            var format = culture.DateTimeFormat;
            if (!string.IsNullOrWhiteSpace(format.AMDesignator) && !string.IsNullOrWhiteSpace(format.PMDesignator) &&
                !string.Equals(format.AMDesignator, format.PMDesignator, StringComparison.OrdinalIgnoreCase))
                return format;
            var filled = (DateTimeFormatInfo)format.Clone();
            filled.AMDesignator = "AM";
            filled.PMDesignator = "PM";
            return filled;
        }

        /// <summary>A time of day: "1:05 PM" or "13:05", or the culture's own short time.</summary>
        public static string Time(DateTime t) => Time(t, CultureInfo.CurrentCulture);

        public static string Time(DateTime t, CultureInfo culture) => _time switch
        {
            TimeStyle.TwelveHour     => t.ToString("h:mm tt", ClockFormat(culture)),
            TimeStyle.TwentyFourHour => t.ToString("HH:mm", culture),
            _ => Uses12Hour(culture) ? t.ToString("h:mm tt", ClockFormat(culture)) : t.ToString("HH:mm", culture),
        };

        /// <summary>The short form month chips use, where there is no room for AM/PM.</summary>
        public static string CompactTime(DateTime t) =>
            t.ToString(Uses12Hour(CultureInfo.CurrentCulture) ? "h:mm" : "H:mm", CultureInfo.CurrentCulture);

        /// <summary>A time gutter label: "1 PM" or "13:00".</summary>
        public static string HourLabel(int hour)
        {
            var at = DateTime.Today.AddHours(hour);
            var culture = CultureInfo.CurrentCulture;
            return Uses12Hour(culture) ? at.ToString("h tt", ClockFormat(culture)) : at.ToString("HH:mm", culture);
        }

        /// <summary>Month and day in the chosen date style, without the year: "10/22", "22/10",
        /// "10-22", or the culture's short date with its year removed.</summary>
        public static string MonthDay(DateTime d) => d.ToString(_current switch
        {
            DateStyle.Iso => "MM-dd",
            DateStyle.US  => "M/d",
            DateStyle.EU  => "dd/MM",
            _ => WithoutYear(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern),
        }, CultureInfo.CurrentCulture);

        /// <summary>Drops the year and the separator that went with it from a short date pattern.</summary>
        private static string WithoutYear(string pattern)
        {
            int start = pattern.IndexOf('y');
            if (start < 0) return pattern;
            int end = pattern.LastIndexOf('y') + 1;
            if (end < pattern.Length && !char.IsLetter(pattern[end]) && start == 0) end++;
            else if (start > 0 && !char.IsLetter(pattern[start - 1])) start--;
            string trimmed = pattern.Remove(start, end - start).Trim();
            return trimmed.Length == 0 ? "M/d" : trimmed;
        }

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
