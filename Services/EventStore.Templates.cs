using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Killendar.Models;

namespace Killendar.Services
{
    public partial class EventStore
    {
        // ============================================================
        // Templates and overlaps. A template is an appointment's details without its people,
        // identity or date: title, place, notes, categories, reminder, all-day, the time of day it
        // starts and how long it runs. It lives in the calendar file, encrypted with it, named by
        // its title. Overlaps are reported after a save and never stop one.
        // ============================================================

        /// <summary>Stores <paramref name="source"/> as a template named by its title, replacing
        /// one of the same name.</summary>
        public void SaveTemplate(CalendarEvent source)
        {
            if (_db == null || string.IsNullOrWhiteSpace(source.Title)) return;
            var template = new CalendarEvent
            {
                Title = source.Title.Trim(), Location = source.Location, Description = source.Description,
                Categories = source.Categories, ReminderMinutes = source.ReminderMinutes, AllDay = source.AllDay,
                Start = source.Start, End = source.End,
            };
            lock (_writeGate)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "INSERT INTO templates(name, json, updated_utc) VALUES($n, $j, $u) " +
                                  "ON CONFLICT(name) DO UPDATE SET json=$j, updated_utc=$u";
                cmd.Parameters.AddWithValue("$n", template.Title);
                cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(template, DeletedJson));
                cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Templates by name.</summary>
        public List<CalendarEvent> ListTemplates()
        {
            var list = new List<CalendarEvent>();
            if (_db == null || !TableExists("templates")) return list;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT json FROM templates ORDER BY name COLLATE NOCASE";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                try { if (JsonSerializer.Deserialize<CalendarEvent>(r.GetString(0), DeletedJson) is CalendarEvent t) list.Add(t); }
                catch (JsonException) { /* an unreadable template is skipped, not fatal */ }
            }
            return list;
        }

        public void DeleteTemplate(string name)
        {
            if (_db == null) return;
            lock (_writeGate)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM templates WHERE name = $n";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Timed, busy appointments that share time with <paramref name="ev"/>: not itself,
        /// not its own series, not all-day ones and not ones marked free (TRANSP:TRANSPARENT).</summary>
        public List<CalendarEvent> Overlapping(CalendarEvent ev)
        {
            if (ev.AllDay || IsFree(ev) || ev.End <= ev.Start) return [];
            Guid series = ev.SeriesId ?? ev.Id;
            return [.. GetInRange(ev.Start, ev.End).Where(o =>
                o.Id != ev.Id && o.Id != series && o.SeriesId != series && !o.AllDay && !IsFree(o) &&
                o.Start < ev.End && o.End > ev.Start)];
        }

        private static bool IsFree(CalendarEvent ev) => MeetingInfo.IsFree(ev);
    }
}
