using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Killendar.Services
{
    /// <summary>
    /// What a support request needs, copied only when someone clicks "Copy diagnostics" in About,
    /// and never sent anywhere by Killendar. It names the version, Windows, the settings that
    /// change behavior, how many appointments there are and what kind of trouble was last seen.
    /// It never carries an appointment's text, a name, an attendee's address, a password, a file
    /// or folder path, or a link: counts, choices and categories only.
    /// </summary>
    internal static class Diagnostics
    {
        internal static string Build(EventStore store)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("Killendar " + AppInfo.Version + (AppInfo.ReleaseDate.Length > 0 ? " (" + AppInfo.ReleaseDate + ")" : ""));
            sb.AppendLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " 64-bit" : " 32-bit"));
            sb.AppendLine(".NET Framework " + Environment.Version);
            sb.AppendLine("Language " + LocaleManager.Current + ", Windows culture " + CultureInfo.CurrentCulture.Name);
            sb.AppendLine("Theme " + ThemeManager.Current + ", accent " + ThemeManager.AccentChoiceFor(ThemeManager.Current));
            sb.AppendLine("Dates " + DateFormatManager.Current + ", clock " + DateFormatManager.CurrentTime +
                          ", week starts " + WeekStartManager.Current);
            sb.AppendLine("Time zones " + (TimeZones.Show ? "shown" : "off") + ", second zone " + (TimeZones.Second != null ? "set" : "none"));

            sb.AppendLine();
            if (!store.IsOpen)
                sb.AppendLine("Killendar: not open (" + Problem(store) + ")");
            else
            {
                var events = store.Events;
                sb.AppendLine("Killendar: open, " + (store.HasPassword ? "encrypted" : "not encrypted"));
                sb.AppendLine("Appointments " + events.Count.ToString(inv) +
                              ", repeating " + events.Count(e => e.IsSeries).ToString(inv) +
                              ", single-date edits " + events.Count(e => e.IsOverride).ToString(inv) +
                              ", with a reminder " + events.Count(e => e.ReminderMinutes.HasValue).ToString(inv) +
                              ", with a time zone " + events.Count(e => TimeZones.ZoneOf(e).Length > 0).ToString(inv));
                sb.AppendLine("Categories " + store.Events.SelectMany(e => EventStore.SplitCategories(e.Categories)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(inv));
                try
                {
                    var backups = store.ListBackups();
                    sb.AppendLine("Backups " + backups.Count.ToString(inv) + (backups.Count > 0
                        ? ", newest " + Math.Max(0, (int)(DateTime.Now - backups.Max(b => b.Taken)).TotalDays).ToString(inv) + " days old"
                        : ""));
                }
                catch (Exception ex) { sb.AppendLine("Backups unreadable (" + ex.GetType().Name + ")"); }
            }
            var shown = ShownCalendars.Items;
            sb.AppendLine("Shown beside it " + shown.Count(i => i.State == ShownCalendars.State.Shown).ToString(inv) +
                          ", locked " + shown.Count(i => i.State == ShownCalendars.State.Locked).ToString(inv) +
                          ", unreadable " + shown.Count(i => i.State == ShownCalendars.State.Unavailable).ToString(inv));

            sb.AppendLine();
            try
            {
                var crash = new FileInfo(App.CrashLogPath);
                sb.AppendLine(crash.Exists
                    ? "Crash log: yes, last written " + crash.LastWriteTime.ToString("yyyy-MM-dd", inv)
                    : "Crash log: none");
            }
            catch { sb.AppendLine("Crash log: unknown"); }
            return sb.ToString();
        }

        /// <summary>The kind of trouble opening the calendar, never the message itself (which can
        /// hold a path).</summary>
        private static string Problem(EventStore store) =>
            store.NeedsPassword ? "waiting for its password"
            : store.LoadError != null ? "could not be read"
            : "locked or closed";
    }
}
