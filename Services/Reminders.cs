using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>One reminder whose time has come, named by the occurrence it is for.</summary>
    public sealed record DueReminder(Guid Id, DateTime Occurrence, string Title, string Location,
        DateTime Start, DateTime End, bool AllDay, DateTime Fire);

    /// <summary>
    /// When reminders fire, and how they travel in calendar files. A reminder is minutes before
    /// the start; an all-day appointment's reminder is 9:00 the day before, whatever the minutes,
    /// because midnight is nobody's reminder. Killendar reminds only while it runs (in the tray
    /// counts); what came due while it was closed shows at the next launch.
    /// </summary>
    public static class Reminders
    {
        /// <summary>The editor's choices, in minutes before the start. An imported reminder with
        /// another lead keeps it and is listed alongside these.</summary>
        public static readonly int[] Choices = [0, 5, 10, 15, 30, 60, 1440];

        public static readonly int[] SnoozeChoices = [5, 10, 15, 60];

        /// <summary>The setting holding the reminder new appointments start with; empty is none.</summary>
        public const string DefaultSetting = "DefaultReminderMinutes";

        /// <summary>How long after an appointment ends its missed reminder is still worth showing.</summary>
        public static readonly TimeSpan MissedGrace = TimeSpan.FromDays(1);

        private const int AllDayHour = 9;

        public static DateTime? FireTime(CalendarEvent ev)
        {
            if (ev.ReminderMinutes is not int minutes) return null;
            try
            {
                return ev.AllDay ? ev.Start.Date.AddDays(-1).AddHours(AllDayHour) : ev.Start.AddMinutes(-minutes);
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        /// <summary>How far before its start an appointment can remind, for the range to scan.</summary>
        public static TimeSpan Lead(CalendarEvent ev) =>
            ev.ReminderMinutes is not int minutes ? TimeSpan.Zero
            : ev.AllDay ? TimeSpan.FromHours(24 - AllDayHour) : TimeSpan.FromMinutes(minutes);

        /// <summary>What a reminder choice reads as. An all-day appointment says when it will
        /// actually come: the day before, at 9:00 in the user's clock.</summary>
        public static string Label(int? minutes, bool allDay, Func<string, string> loc)
        {
            if (minutes is not int m) return loc("Str_Rem_None");
            if (allDay) return string.Format(loc("Str_Rem_DayBeforeAt"), DateFormatManager.Time(DateTime.Today.AddHours(AllDayHour)));
            if (m == 0) return loc("Str_Rem_AtStart");
            if (m == 60) return loc("Str_Rem_Hour");
            if (m == 1440) return loc("Str_Rem_Day");
            if (m % 1440 == 0) return string.Format(loc("Str_Rem_Days"), m / 1440);
            if (m % 60 == 0) return string.Format(loc("Str_Rem_Hours"), m / 60);
            return string.Format(loc("Str_Rem_Minutes"), m);
        }

        public static int? ParseDefault(string? setting) =>
            int.TryParse(setting, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) &&
            Array.IndexOf(Choices, minutes) >= 0 ? minutes : null;

        // ---- calendar files ----

        private static readonly Regex Duration = new(
            @"^(?<sign>[+-])?P(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<m>\d+)M)?(?:(?<s>\d+)S)?)?$",
            RegexOptions.CultureInvariant);

        /// <summary>The reminder an imported appointment's first alarm sets: a display or sound
        /// alarm triggered at or before the start. Alarms at a fixed time, after the start or
        /// relative to the end are kept in the file but set no reminder here.</summary>
        public static int? FromAlarms(IEnumerable<string> keptLines)
        {
            bool inAlarm = false;
            string? action = null;
            int? trigger = null;
            foreach (var line in keptLines)
            {
                string upper = line.ToUpperInvariant();
                if (upper.StartsWith("BEGIN:VALARM", StringComparison.Ordinal)) { inAlarm = true; action = null; trigger = null; continue; }
                if (!inAlarm) continue;
                if (upper.StartsWith("END:VALARM", StringComparison.Ordinal))
                {
                    inAlarm = false;
                    if (trigger.HasValue && (action == null || action == "DISPLAY" || action == "AUDIO")) return trigger;
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string head = upper.Substring(0, colon);
                string value = line.Substring(colon + 1).Trim();
                if (head == "ACTION") action = value.ToUpperInvariant();
                else if (head == "TRIGGER" || head.StartsWith("TRIGGER;", StringComparison.Ordinal))
                {
                    if (head.Contains("VALUE=DATE-TIME") || head.Contains("RELATED=END")) continue;
                    trigger = MinutesBefore(value);
                }
            }
            return null;
        }

        private static int? MinutesBefore(string value)
        {
            var m = Duration.Match(value.ToUpperInvariant());
            if (!m.Success) return null;
            long Part(string name) => m.Groups[name].Success ? long.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture) : 0;
            long minutes = Part("w") * 7 * 1440 + Part("d") * 1440 + Part("h") * 60 + Part("m") + (Part("s") + 59) / 60;
            if (minutes == 0) return 0;
            if (m.Groups["sign"].Value != "-" || minutes > int.MaxValue) return null;
            return (int)minutes;
        }

        /// <summary>The alarm export writes for a reminder set here.</summary>
        public static IEnumerable<string> AlarmLines(CalendarEvent ev)
        {
            if (ev.ReminderMinutes is not int minutes) yield break;
            yield return "BEGIN:VALARM";
            yield return "ACTION:DISPLAY";
            yield return "DESCRIPTION:Reminder";
            yield return "TRIGGER:" + Trigger(ev.AllDay ? (24 - AllDayHour) * 60 : minutes);
            yield return "END:VALARM";
        }

        private static string Trigger(int minutes)
        {
            if (minutes == 0) return "PT0S";
            int days = minutes / 1440, hours = minutes % 1440 / 60, rest = minutes % 60;
            string time = (hours > 0 ? hours + "H" : "") + (rest > 0 ? rest + "M" : "");
            return "-P" + (days > 0 ? days + "D" : "") + (time.Length > 0 ? "T" + time : "");
        }
    }
}
