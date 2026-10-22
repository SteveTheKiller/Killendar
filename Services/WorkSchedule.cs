using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Killendar.Services
{
    /// <summary>
    /// The days and hours someone works, Monday to Friday 9 to 17 until they say otherwise. The
    /// work week shows from the first workday of the week to the last, so Sunday to Thursday or
    /// Tuesday to Saturday are whole weeks too; a day off inside that range is shaded, not
    /// dropped, which keeps every column one day after the last. Hours outside the working day
    /// are shaded in Week and Day.
    /// </summary>
    public static class WorkSchedule
    {
        public const string DaysSetting = "WorkDays";
        public const string HoursSetting = "WorkHours";

        private static readonly DayOfWeek[] DefaultDays =
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

        public static IReadOnlyCollection<DayOfWeek> Days { get; private set; } = new HashSet<DayOfWeek>(DefaultDays);
        public static int StartHour { get; private set; } = 9;
        public static int EndHour { get; private set; } = 17;

        /// <summary>Reads the two settings; anything unreadable keeps the default.</summary>
        public static void Load(string? days, string? hours)
        {
            var parsed = new HashSet<DayOfWeek>();
            foreach (var part in (days ?? "").Split(','))
                if (int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int d) && d >= 0 && d <= 6)
                    parsed.Add((DayOfWeek)d);
            Days = parsed.Count > 0 ? parsed : new HashSet<DayOfWeek>(DefaultDays);

            StartHour = 9; EndHour = 17;
            var range = (hours ?? "").Split('-');
            if (range.Length == 2 &&
                int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out int start) &&
                int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out int end) &&
                start >= 0 && end <= 24 && start < end)
            {
                StartHour = start; EndHour = end;
            }
        }

        public static string FormatDays(IEnumerable<DayOfWeek> days) =>
            string.Join(",", days.Distinct().OrderBy(d => (int)d).Select(d => ((int)d).ToString(CultureInfo.InvariantCulture)));

        public static string FormatHours(int start, int end) =>
            start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture);

        public static bool IsWorkday(DayOfWeek day) => Days.Contains(day);

        public static bool IsWorkingHour(int hour) => hour >= StartHour && hour < EndHour;

        /// <summary>The work week holding <paramref name="anchor"/>: its first workday and how many
        /// days run to its last one, in the week that starts on <paramref name="weekStart"/>.</summary>
        public static (DateTime First, int Count) WorkWeek(DateTime anchor, DayOfWeek weekStart)
        {
            var weekFirst = anchor.Date.AddDays(-(((int)anchor.DayOfWeek - (int)weekStart + 7) % 7));
            int first = -1, last = -1;
            for (int i = 0; i < 7; i++)
            {
                if (!IsWorkday(weekFirst.AddDays(i).DayOfWeek)) continue;
                if (first < 0) first = i;
                last = i;
            }
            return first < 0 ? (weekFirst, 7) : (weekFirst.AddDays(first), last - first + 1);
        }
    }
}
