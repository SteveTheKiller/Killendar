using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Killendar.Models;

namespace Killendar.Services
{
    public partial class EventStore
    {
        // ============================================================
        // In-memory mirror
        // ============================================================

        private const string SelectAll =
            "SELECT id, title, start_utc, end_utc, all_day, location, description, attendees, " +
            "created_utc, modified_utc, categories, repeat_freq, repeat_interval, repeat_days, " +
            "repeat_until, repeat_count, skip_dates, series_id, occurrence_start FROM events";

        private void LoadIntoMemory()
        {
            var list = new List<CalendarEvent>();
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = SelectAll;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    bool allDay = r.GetInt64(4) != 0;
                    list.Add(new CalendarEvent
                    {
                        Id          = Guid.TryParse(r.GetString(0), out var g) ? g : Guid.NewGuid(),
                        Title       = r.GetString(1),
                        Start       = FromStore(r.GetString(2), allDay),
                        End         = FromStore(r.GetString(3), allDay),
                        AllDay      = allDay,
                        Location    = r.GetString(5),
                        Description = r.GetString(6),
                        Attendees   = SplitAttendees(r.GetString(7)),
                        Created     = FromStoreUtc(r.GetString(8)),
                        Modified    = FromStoreUtc(r.GetString(9)),
                        Categories  = r.IsDBNull(10) ? "" : r.GetString(10),

                        // Repeat columns. Every one is IsDBNull-guarded: an ALTER TABLE ADD COLUMN
                        // on an existing row fills it with the default, but a database touched by
                        // another SQLite tool may not have, and a null here must read as "plain
                        // appointment" rather than throw the whole Killendar's load away.
                        Repeat          = r.IsDBNull(11) ? RepeatFreq.None : ToFreq(r.GetInt64(11)),
                        RepeatInterval  = r.IsDBNull(12) ? 1 : Math.Max(1, (int)r.GetInt64(12)),
                        RepeatDays      = SplitDays(r.IsDBNull(13) ? "" : r.GetString(13)),
                        RepeatUntil     = FromStoreDate(r.IsDBNull(14) ? "" : r.GetString(14)),
                        RepeatCount     = r.IsDBNull(15) ? 0 : Math.Max(0, (int)r.GetInt64(15)),
                        SkipDates       = SplitDates(r.IsDBNull(16) ? "" : r.GetString(16)),
                        SeriesId        = ParseGuid(r.IsDBNull(17) ? "" : r.GetString(17)),
                        OccurrenceStart = r.IsDBNull(18)
                                            ? null
                                            : FromStoreNullable(r.GetString(18), allDay),
                    });
                }
            }
            _events = list;
        }

        /// <summary>Re-reads the open database. Public because the unlock and switch paths call
        /// it, and because Calendar.cs used to call Load() on the JSON store.</summary>
        public void Load()
        {
            if (_db == null) { _events = []; return; }
            LoadError = null;
            try { LoadIntoMemory(); }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                _events = [];
            }
        }

        // ============================================================
        // Mutations. Each writes through to the database, then repaints.
        // ============================================================

        private void Upsert(CalendarEvent ev) => Upsert(_db!, ev);

        // Takes the connection explicitly so migration can write to a database that is not yet
        // the open one. CreateCommand() picks up the connection's current transaction on its own.
        private static void Upsert(SqliteConnection db, CalendarEvent ev)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "INSERT INTO events(id, title, start_utc, end_utc, all_day, location, " +
                "description, attendees, categories, created_utc, modified_utc, " +
                "repeat_freq, repeat_interval, repeat_days, repeat_until, repeat_count, " +
                "skip_dates, series_id, occurrence_start) " +
                "VALUES($id, $ti, $st, $en, $ad, $lo, $de, $at, $ca, $cr, $mo, " +
                "$rf, $ri, $rd, $ru, $rc, $sk, $si, $os) " +
                "ON CONFLICT(id) DO UPDATE SET title=$ti, start_utc=$st, end_utc=$en, " +
                "all_day=$ad, location=$lo, description=$de, attendees=$at, categories=$ca, " +
                "modified_utc=$mo, repeat_freq=$rf, repeat_interval=$ri, repeat_days=$rd, " +
                "repeat_until=$ru, repeat_count=$rc, skip_dates=$sk, series_id=$si, " +
                "occurrence_start=$os";
            cmd.Parameters.AddWithValue("$id", ev.Id.ToString());
            cmd.Parameters.AddWithValue("$ti", ev.Title ?? "");
            cmd.Parameters.AddWithValue("$st", ToStore(ev.Start, ev.AllDay));
            cmd.Parameters.AddWithValue("$en", ToStore(ev.End, ev.AllDay));
            cmd.Parameters.AddWithValue("$ad", ev.AllDay ? 1 : 0);
            cmd.Parameters.AddWithValue("$lo", ev.Location ?? "");
            cmd.Parameters.AddWithValue("$de", ev.Description ?? "");
            cmd.Parameters.AddWithValue("$at", JoinAttendees(ev.Attendees ?? []));
            cmd.Parameters.AddWithValue("$ca", NormalizeCategories(ev.Categories));
            cmd.Parameters.AddWithValue("$cr", ToStoreUtc(ev.Created));
            cmd.Parameters.AddWithValue("$mo", ToStoreUtc(ev.Modified));
            cmd.Parameters.AddWithValue("$rf", (int)ev.Repeat);
            cmd.Parameters.AddWithValue("$ri", ev.RepeatInterval > 0 ? ev.RepeatInterval : 1);
            cmd.Parameters.AddWithValue("$rd", JoinDays(ev.RepeatDays ?? []));
            cmd.Parameters.AddWithValue("$ru", ToStoreDate(ev.RepeatUntil));
            cmd.Parameters.AddWithValue("$rc", ev.RepeatCount > 0 ? ev.RepeatCount : 0);
            cmd.Parameters.AddWithValue("$sk", JoinDates(ev.SkipDates ?? []));
            cmd.Parameters.AddWithValue("$si", ev.SeriesId?.ToString() ?? "");
            cmd.Parameters.AddWithValue("$os", ev.OccurrenceStart.HasValue
                ? ToStore(ev.OccurrenceStart.Value, ev.AllDay) : "");
            cmd.ExecuteNonQuery();
        }

        /// <summary>Writes, then repaints. If persistence fails, reload the database so the
        /// calendar never presents an unsaved mutation as a saved appointment.</summary>
        private bool Commit(Action write, bool refreshCategories = false)
        {
            try
            {
                if (_db == null) throw new InvalidOperationException("No Killendar is open.");
                write();
                if (refreshCategories) CategoryManager.Refresh(this);
                LoadError = null;
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                try
                {
                    if (_db != null) LoadIntoMemory(); else _events = [];
                    if (refreshCategories) CategoryManager.Refresh(this);
                }
                catch (Exception reloadError)
                {
                    _events = [];
                    LoadError = ex.Message + " " + reloadError.Message;
                }
                Changed?.Invoke();
                return false;
            }
            Changed?.Invoke();
            return true;
        }

        public void Add(CalendarEvent ev)
        {
            ev.Created = ev.Modified = DateTime.UtcNow;
            _events.Add(ev);
            Commit(() => Upsert(ev));
        }

        public void Update(CalendarEvent ev)
        {
            var idx = _events.FindIndex(e => e.Id == ev.Id);
            if (idx < 0) return;
            ev.Modified = DateTime.UtcNow;
            _events[idx] = ev;
            Commit(() => Upsert(ev));
        }

        public void Delete(Guid id)
        {
            if (_events.RemoveAll(e => e.Id == id) == 0) return;
            Commit(() =>
            {
                using var cmd = _db!.CreateCommand();
                cmd.CommandText = "DELETE FROM events WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id.ToString());
                cmd.ExecuteNonQuery();
            });
        }

        // ============================================================
        // Series. The four things "this one or the whole series" can mean.
        // ============================================================

        /// <summary>
        /// Applies an edit to the WHOLE series. Moving the appointment moves every occurrence by
        /// the same amount, which is what editing the series from one of its dates means - the
        /// master keeps being the anchor of the pattern rather than jumping to the date the user
        /// happened to be looking at.
        /// </summary>
        public void UpdateSeries(CalendarEvent edited)
        {
            var master = GetSeriesMaster(edited);
            if (master == null) { Update(edited); return; }

            Commit(() =>
            {
                var duration = edited.End > edited.Start ? edited.End - edited.Start : TimeSpan.FromHours(1);
                var origin = edited.OccurrenceStart ?? master.Start;
                var delta = edited.Start - origin;
                var updated = master.Clone();
                updated.Title       = edited.Title;
                updated.Location    = edited.Location;
                updated.Description = edited.Description;
                updated.Attendees   = [.. edited.Attendees ?? []];
                updated.Categories  = edited.Categories;
                updated.AllDay      = edited.AllDay;
                updated.Start       = master.Start + delta;
                updated.End         = updated.Start + duration;
                updated.Repeat         = edited.Repeat;
                updated.RepeatInterval = edited.RepeatInterval > 0 ? edited.RepeatInterval : 1;
                updated.RepeatDays     = [.. edited.RepeatDays ?? []];
                updated.RepeatUntil    = edited.RepeatUntil;
                updated.RepeatCount    = edited.RepeatCount;
                updated.Modified       = DateTime.UtcNow;

                // Recover each deleted occurrence's time before shifting its date. A move
                // across midnight can change the day even when delta is less than 24 hours.
                updated.SkipDates = master.SkipDates
                    .Select(day => (day.Date + master.Start.TimeOfDay + delta).Date)
                    .Where(day => Recurrence.GeneratesDate(updated, day))
                    .ToList();

                var retained = new List<CalendarEvent>();
                var removed = new HashSet<Guid>();
                foreach (var exception in _events.Where(e => e.SeriesId == master.Id))
                {
                    var identity = exception.OccurrenceStart + delta;
                    if (!identity.HasValue || !Recurrence.GeneratesDate(updated, identity.Value))
                    {
                        removed.Add(exception.Id);
                        continue;
                    }
                    var shifted = exception.Clone();
                    shifted.OccurrenceStart = identity;
                    shifted.Start += delta;
                    shifted.End += delta;
                    shifted.Modified = updated.Modified;
                    retained.Add(shifted);
                }

                using var tx = _db!.BeginTransaction();
                Upsert(updated);
                foreach (var exception in retained) Upsert(exception);
                foreach (var id in removed)
                {
                    using var cmd = _db.CreateCommand();
                    cmd.CommandText = "DELETE FROM events WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", id.ToString());
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();

                _events.RemoveAll(e => removed.Contains(e.Id));
                _events[_events.FindIndex(e => e.Id == master.Id)] = updated;
                foreach (var exception in retained)
                    _events[_events.FindIndex(e => e.Id == exception.Id)] = exception;
            });
        }

        /// <summary>
        /// Saves an edit to ONE date of a series, as a row that stands in for that occurrence.
        /// </summary>
        public void SaveOccurrence(CalendarEvent edited)
        {
            var key = edited.SeriesKey;
            var day = (edited.OccurrenceStart ?? edited.Start).Date;

            var existing = _events.FirstOrDefault(
                e => e.IsOverride && e.SeriesId == key &&
                     e.OccurrenceStart.HasValue && e.OccurrenceStart.Value.Date == day);

            var row = edited.Clone();
            row.SeriesId        = key;
            row.OccurrenceStart = edited.OccurrenceStart ?? edited.Start;

            // An override is one date. The SERIES repeats, not the row that replaces one of its
            // dates - leaving the pattern on here would expand the override too and every later
            // occurrence would appear twice.
            row.Repeat         = RepeatFreq.None;
            row.RepeatInterval = 1;
            row.RepeatDays     = [];
            row.RepeatUntil    = null;
            row.RepeatCount    = 0;
            row.SkipDates      = [];

            if (existing != null)
            {
                row.Id      = existing.Id;
                row.Created = existing.Created;
                Update(row);
            }
            else
            {
                // A NEW id, always. The occurrence handed out by Recurrence carries its master's
                // id, and saving with that would overwrite the series with a single appointment.
                row.Id = Guid.NewGuid();
                Add(row);
            }
        }

        /// <summary>Removes ONE date from a series, leaving the rest of it alone.</summary>
        public void DeleteOccurrence(CalendarEvent occ)
        {
            var key = occ.SeriesKey;
            var day = (occ.OccurrenceStart ?? occ.Start).Date;

            // If that date had been edited singly it is a real row, and it has to go too or the
            // deleted occurrence survives its own deletion.
            var stray = _events.FirstOrDefault(
                e => e.IsOverride && e.SeriesId == key &&
                     e.OccurrenceStart.HasValue && e.OccurrenceStart.Value.Date == day);
            var master = GetSeriesMaster(occ);
            if (master == null) return;
            bool skipped = master.SkipDates.Any(d => d.Date == day);
            if (skipped && stray == null) return;

            var updated = master.Clone();
            if (!skipped) updated.SkipDates.Add(day);
            updated.Modified = DateTime.UtcNow;
            Commit(() =>
            {
                using var tx = _db!.BeginTransaction();
                if (stray != null)
                {
                    using var cmd = _db.CreateCommand();
                    cmd.CommandText = "DELETE FROM events WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", stray.Id.ToString());
                    cmd.ExecuteNonQuery();
                }
                Upsert(updated);
                tx.Commit();
                if (stray != null) _events.RemoveAll(e => e.Id == stray.Id);
                _events[_events.FindIndex(e => e.Id == master.Id)] = updated;
            });
        }

        /// <summary>Removes a whole series: the master and every single-date edit belonging to it.</summary>
        public void DeleteSeries(Guid masterId)
        {
            var ids = _events.Where(e => e.Id == masterId || e.SeriesId == masterId)
                             .Select(e => e.Id).ToList();
            if (ids.Count == 0) return;

            var gone = new HashSet<Guid>(ids);
            _events.RemoveAll(e => gone.Contains(e.Id));

            Commit(() =>
            {
                using var tx = _db!.BeginTransaction();
                foreach (var id in ids)
                {
                    using var cmd = _db.CreateCommand();
                    cmd.CommandText = "DELETE FROM events WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", id.ToString());
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            });
        }
    }
}
