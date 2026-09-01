using System;

namespace Killendar.Services
{
    public enum WeekStartStyle { FollowWindows, Sunday, Monday }

    public static class WeekStartManager
    {
        public static Func<string, string?> GetSetting { get; set; } = _ => null;
        public static Action<string, string> SetSetting { get; set; } = (_, _) => { };

        private static WeekStartStyle _current = WeekStartStyle.FollowWindows;
        private static DayOfWeek _windowsFirstDay = DayOfWeek.Sunday;

        public static WeekStartStyle Current => _current;
        public static DayOfWeek FirstDay => _current switch
        {
            WeekStartStyle.Sunday => DayOfWeek.Sunday,
            WeekStartStyle.Monday => DayOfWeek.Monday,
            _ => _windowsFirstDay,
        };

        public static void Initialize(DayOfWeek windowsFirstDay)
        {
            _windowsFirstDay = windowsFirstDay;
            _current = Enum.TryParse<WeekStartStyle>(GetSetting("WeekStart"), out var style)
                ? style : WeekStartStyle.FollowWindows;
        }

        public static void Apply(WeekStartStyle style)
        {
            _current = style;
            SetSetting("WeekStart", style.ToString());
        }
    }
}
