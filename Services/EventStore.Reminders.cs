using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using Killendar.Models;

namespace Killendar.Services
{
    public partial class EventStore
    {
        // ============================================================
        // Reminders. When each one fires is worked out from the appointments (Reminders.cs);
        // only what the user did about one - dismissed it, or snoozed it until when - is stored,
        // inside the calendar file, so it survives a restart and stays encrypted with the rest.
        // ============================================================

        private static readonly TimeSpan ReminderStateRetention = TimeSpan.FromDays(30);

        /// <summary>Reminders whose time has come and that were neither dismissed nor snoozed past
        /// now, earliest first. Includes those missed while Killendar was closed, until a day after
        /// their appointment ended, and only the latest date of a repeating one.</summary>
        public List<DueReminder> DueReminders(DateTime now)
        {
            if (_db == null) return [];
            if (!_events.Any(e => e.ReminderMinutes.HasValue)) return [];
            var lead = _events.Max(Reminders.Lead);
            var states = ReminderStates();
            // A missed date of a series is overtaken by the next one: only the latest due date of
            // each appointment counts, and dismissing or snoozing it quiets the older ones too.
            var latest = new Dictionary<Guid, (CalendarEvent Occ, DateTime Fire)>();
            foreach (var occ in GetInRange(now - Reminders.MissedGrace - lead, now + lead + TimeSpan.FromMinutes(1)))
            {
                if (Reminders.FireTime(occ) is not DateTime fire || fire > now) continue;
                if (occ.End <= now - Reminders.MissedGrace) continue;
                if (!latest.TryGetValue(occ.Id, out var seen) || occ.Start > seen.Occ.Start) latest[occ.Id] = (occ, fire);
            }
            var due = new List<DueReminder>();
            foreach (var (occ, fire) in latest.Values)
            {
                if (states.TryGetValue((occ.Id, OccurrenceKey(occ)), out var state) &&
                    (state.Dismissed || state.SnoozeUntil > now)) continue;
                due.Add(new DueReminder(occ.Id, occ.Start, occ.Title, occ.Location, occ.Start, occ.End, occ.AllDay,
                    state.SnoozeUntil > fire ? state.SnoozeUntil : fire));
            }
            return [.. due.OrderBy(d => d.Fire).ThenBy(d => d.Start)];
        }

        /// <summary>When reminders will next come due in the coming span, snoozes included. Taken
        /// before a lock so the locked window can count arrivals without reading the calendar.</summary>
        public List<DateTime> UpcomingReminderTimes(DateTime now, TimeSpan ahead)
        {
            if (_db == null) return [];
            var states = ReminderStates();
            var times = new List<DateTime>();
            var lead = _events.Count == 0 ? TimeSpan.Zero : _events.Max(Reminders.Lead);
            foreach (var occ in GetInRange(now, now + ahead + lead))
                if (Reminders.FireTime(occ) is DateTime fire && fire > now && fire <= now + ahead &&
                    !(states.TryGetValue((occ.Id, OccurrenceKey(occ)), out var state) && state.Dismissed))
                    times.Add(fire);
            foreach (var state in states.Values)
                if (!state.Dismissed && state.SnoozeUntil > now && state.SnoozeUntil <= now + ahead) times.Add(state.SnoozeUntil);
            times.Sort();
            return times;
        }

        public void DismissReminder(DueReminder reminder) => SaveReminderState(reminder, dismissed: true, DateTime.MinValue);

        public void SnoozeReminder(DueReminder reminder, DateTime until) => SaveReminderState(reminder, dismissed: false, until);

        private void SaveReminderState(DueReminder reminder, bool dismissed, DateTime snoozeUntil)
        {
            if (_db == null) return;
            lock (_writeGate)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText =
                    "INSERT INTO reminder_state(event_id, occurrence, dismissed, snooze_until, updated_utc) " +
                    "VALUES($id, $occ, $dis, $until, $now) ON CONFLICT(event_id, occurrence) DO UPDATE SET " +
                    "dismissed=$dis, snooze_until=$until, updated_utc=$now";
                cmd.Parameters.AddWithValue("$id", reminder.Id.ToString());
                cmd.Parameters.AddWithValue("$occ", ToStore(reminder.Occurrence, reminder.AllDay));
                cmd.Parameters.AddWithValue("$dis", dismissed ? 1 : 0);
                cmd.Parameters.AddWithValue("$until", snoozeUntil == DateTime.MinValue ? ""
                    : snoozeUntil.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                // Not LoadError: that would block backups and read as a failed undo. A reminder that
                // could not be marked simply shows again on the next check.
                try { cmd.ExecuteNonQuery(); }
                catch (SqliteException) { }
            }
        }

        private static string OccurrenceKey(CalendarEvent occ) => ToStore(occ.Start, occ.AllDay);

        private Dictionary<(Guid, string), (bool Dismissed, DateTime SnoozeUntil)> ReminderStates()
        {
            var states = new Dictionary<(Guid, string), (bool, DateTime)>();
            if (!TableExists("reminder_state")) return states;
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "SELECT event_id, occurrence, dismissed, snooze_until FROM reminder_state";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!Guid.TryParse(r.GetString(0), out var id)) continue;
                var until = DateTime.TryParse(r.GetString(3), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var u) ? u.ToLocalTime() : DateTime.MinValue;
                states[(id, r.GetString(1))] = (r.GetInt64(2) != 0, until);
            }
            return states;
        }

        /// <summary>Forgets what was done about reminders a month on; their appointments are past.</summary>
        internal void PurgeReminderState(DateTime nowUtc)
        {
            if (_db == null || !TableExists("reminder_state")) return;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM reminder_state WHERE updated_utc < $cut";
            cmd.Parameters.AddWithValue("$cut", (nowUtc - ReminderStateRetention).ToString("o", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
    }
}
