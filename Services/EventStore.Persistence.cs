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
            "repeat_until, repeat_count, skip_dates, series_id, occurrence_start, " +
            "repeat_time_basis, repeat_anchor, repeat_zone_id, repeat_week_start, " +
            "skip_occurrences, repeat_until_exact, occurrence_anchor, occurrence_basis, occurrence_zone_id, " +
            "duration_days, duration_time_ticks, external_uid, kept_properties FROM events";

        private void LoadIntoMemory()
        {
            var list = new List<CalendarEvent>();
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var schema = _db!.CreateCommand())
            {
                schema.CommandText = "PRAGMA table_info(events)";
                using var columns = schema.ExecuteReader();
                while (columns.Read()) available.Add(columns.GetString(1));
            }
            // Read-only callers cannot migrate historical calendars. Missing additive columns
            // use the row decoder's existing defaults without writing to the database.
            var projection = SelectAll.Substring(7, SelectAll.Length - 7 - " FROM events".Length)
                .Split(',').Select(name => name.Trim()).Select((name, index) =>
                    index >= 10 && !available.Contains(name) ? "NULL AS " + name : name);
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "SELECT " + string.Join(", ", projection) + " FROM events";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    bool allDay = r.GetInt64(4) != 0;
                    bool originalAllDay = r.IsDBNull(25) || string.IsNullOrEmpty(r.GetString(25))
                        ? allDay : !r.IsDBNull(26) && r.GetInt64(26) == (long)OccurrenceTimeBasis.Date;
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
                                            : FromStoreNullable(r.GetString(18), originalAllDay),
                        RepeatTimeBasis = r.IsDBNull(19) || r.GetInt64(19) < 0 || r.GetInt64(19) > 2
                            ? RecurrenceTimeBasis.Local : (RecurrenceTimeBasis)r.GetInt64(19),
                        RepeatAnchor = r.IsDBNull(20) ? null : FromStoreNullable(r.GetString(20), true),
                        RepeatZoneId = r.IsDBNull(21) ? "" : r.GetString(21),
                        RepeatWeekStart = r.IsDBNull(22) || r.GetInt64(22) < 0 || r.GetInt64(22) > 6
                            ? DayOfWeek.Sunday : (DayOfWeek)r.GetInt64(22),
                        SkipOccurrences = SplitOccurrences(r.IsDBNull(23) ? "" : r.GetString(23)),
                        RepeatUntilExact = FromStoreExact(r.IsDBNull(24) ? "" : r.GetString(24)),
                        OccurrenceAnchor = FromStoreNullable(r.IsDBNull(25) ? "" : r.GetString(25), true),
                        OccurrenceBasis = r.IsDBNull(26) || r.GetInt64(26) < 0 || r.GetInt64(26) > 3
                            ? OccurrenceTimeBasis.Date : (OccurrenceTimeBasis)r.GetInt64(26),
                        OccurrenceZoneId = r.IsDBNull(27) ? "" : r.GetString(27),
                        DurationDays = r.IsDBNull(28) ? 0 : (int)Math.Min(int.MaxValue, Math.Max(0L, r.GetInt64(28))),
                        DurationTimeTicks = r.IsDBNull(29) ? 0 : Math.Max(0L, r.GetInt64(29)),
                        ExternalUid = r.IsDBNull(30) ? "" : r.GetString(30),
                        // One unfolded content line per row; a content line cannot contain a newline.
                        KeptProperties = r.IsDBNull(31) || r.GetString(31).Length == 0
                            ? [] : [.. r.GetString(31).Split('\n')],
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
            using var cmd = CreateUpsert(db);
            BindUpsert(cmd, ev);
            cmd.ExecuteNonQuery();
        }

        /// <summary>One command for a whole batch: SQLite then prepares the statement once
        /// instead of per row, which is most of an import's write time.</summary>
        private static SqliteCommand CreateUpsert(SqliteConnection db)
        {
            var cmd = db.CreateCommand();
            cmd.CommandText =
                "INSERT INTO events(id, title, start_utc, end_utc, all_day, location, " +
                "description, attendees, categories, created_utc, modified_utc, " +
                "repeat_freq, repeat_interval, repeat_days, repeat_until, repeat_count, " +
                "skip_dates, series_id, occurrence_start, repeat_time_basis, repeat_anchor, repeat_zone_id, repeat_week_start, " +
                "skip_occurrences, repeat_until_exact, occurrence_anchor, occurrence_basis, occurrence_zone_id, duration_days, duration_time_ticks, external_uid, kept_properties) " +
                "VALUES($id, $ti, $st, $en, $ad, $lo, $de, $at, $ca, $cr, $mo, " +
                "$rf, $ri, $rd, $ru, $rc, $sk, $si, $os, $tb, $ra, $rz, $ws, $so, $ue, $oa, $ob, $oz, $dd, $dt, $xu, $kp) " +
                "ON CONFLICT(id) DO UPDATE SET title=$ti, start_utc=$st, end_utc=$en, " +
                "all_day=$ad, location=$lo, description=$de, attendees=$at, categories=$ca, " +
                "modified_utc=$mo, repeat_freq=$rf, repeat_interval=$ri, repeat_days=$rd, " +
                "repeat_until=$ru, repeat_count=$rc, skip_dates=$sk, series_id=$si, " +
                "occurrence_start=$os, repeat_time_basis=$tb, repeat_anchor=$ra, repeat_zone_id=$rz, repeat_week_start=$ws, " +
                "skip_occurrences=$so, repeat_until_exact=$ue, occurrence_anchor=$oa, occurrence_basis=$ob, occurrence_zone_id=$oz, " +
                "duration_days=$dd, duration_time_ticks=$dt, external_uid=$xu, kept_properties=$kp";
            return cmd;
        }

        private static void BindUpsert(SqliteCommand cmd, CalendarEvent ev)
        {
            cmd.Parameters.Clear();
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
                ? ToStore(ev.OccurrenceStart.Value, ev.OccurrenceAnchor.HasValue ? ev.OccurrenceBasis == OccurrenceTimeBasis.Date : ev.AllDay) : "");
            cmd.Parameters.AddWithValue("$tb", (int)ev.RepeatTimeBasis);
            cmd.Parameters.AddWithValue("$ra", ev.RepeatAnchor.HasValue ? ToStore(ev.RepeatAnchor.Value, true) : "");
            cmd.Parameters.AddWithValue("$rz", ev.RepeatZoneId ?? "");
            cmd.Parameters.AddWithValue("$ws", (int)ev.RepeatWeekStart);
            cmd.Parameters.AddWithValue("$so", JoinOccurrences(ev.SkipOccurrences ?? []));
            cmd.Parameters.AddWithValue("$ue", ToStoreExact(ev.RepeatUntilExact));
            cmd.Parameters.AddWithValue("$oa", ev.OccurrenceAnchor.HasValue ? ToStore(ev.OccurrenceAnchor.Value, true) : "");
            cmd.Parameters.AddWithValue("$ob", (int)ev.OccurrenceBasis);
            cmd.Parameters.AddWithValue("$oz", ev.OccurrenceZoneId ?? "");
            cmd.Parameters.AddWithValue("$dd", ev.DurationDays);
            cmd.Parameters.AddWithValue("$dt", ev.DurationTimeTicks);
            cmd.Parameters.AddWithValue("$xu", ev.ExternalUid ?? "");
            cmd.Parameters.AddWithValue("$kp", string.Join("\n", ev.KeptProperties ?? []));
        }

        /// <summary>Writes, then repaints. If persistence fails, reload the database so the
        /// calendar never presents an unsaved mutation as a saved appointment.</summary>
        private bool Commit(Action write, bool refreshCategories = false)
        {
            try
            {
                if (_db == null) throw new InvalidOperationException("No Killendar is open.");
                // Waits out an import writing on a worker thread; the connection is not shared.
                lock (_writeGate) write();
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
            // An imported one-off keeps the source clock it arrived with. Once it has been moved,
            // that clock still names the OLD start, and a repeat added now would generate from
            // there. A stored one-off therefore always re-anchors to the start being saved;
            // existing series go through UpdateSeries, which shifts the anchor by the edit.
            if (!_events[idx].IsSeries && !ev.IsOverride && Recurrence.UsesTimeBasis(ev) &&
                Recurrence.FromTimeBasis(ev, ev.RepeatAnchor!.Value) != ev.Start.ToUniversalTime())
                ev.RepeatAnchor = Recurrence.ToTimeBasis(ev, ev.Start);
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
                var nominalDelta = delta;
                var updated = master.Clone();
                updated.Title       = edited.Title;
                updated.Location    = edited.Location;
                updated.Description = edited.Description;
                updated.Attendees   = [.. edited.Attendees ?? []];
                updated.Categories  = edited.Categories;
                updated.AllDay      = edited.AllDay;
                updated.DurationDays = edited.AllDay ? 0 : edited.DurationDays;
                updated.DurationTimeTicks = edited.AllDay ? 0 : edited.DurationTimeTicks;
                updated.Start       = master.Start + delta;
                updated.End         = updated.Start + duration;
                if (Recurrence.UsesTimeBasis(master) && !updated.AllDay)
                {
                    var sourceDelta = edited.Start.ToUniversalTime() == origin.ToUniversalTime()
                        ? TimeSpan.Zero
                        : Recurrence.ToTimeBasis(master, edited.Start) -
                            Recurrence.OriginalTimeBasisStart(master, origin);
                    updated.RepeatAnchor = master.RepeatAnchor!.Value + sourceDelta;
                    nominalDelta = sourceDelta;
                    updated.Start = Recurrence.FromTimeBasis(updated, updated.RepeatAnchor.Value).ToLocalTime();
                    var physicalDuration = edited.End.ToUniversalTime() - edited.Start.ToUniversalTime();
                    if (physicalDuration <= TimeSpan.Zero) physicalDuration = TimeSpan.FromHours(1);
                    updated.End = (updated.Start.ToUniversalTime() + physicalDuration).ToLocalTime();
                    delta = updated.Start - master.Start;
                }
                else if (updated.AllDay)
                {
                    updated.RepeatTimeBasis = RecurrenceTimeBasis.Local;
                    updated.RepeatAnchor = null;
                    updated.RepeatZoneId = "";
                }
                updated.Repeat         = edited.Repeat;
                if (NominalDuration.HasValue(updated))
                    updated.End = NominalDuration.EndAfterStartChange(updated, updated.Start);
                updated.RepeatInterval = edited.RepeatInterval > 0 ? edited.RepeatInterval : 1;
                updated.RepeatDays     = [.. edited.RepeatDays ?? []];
                updated.RepeatUntil    = edited.RepeatUntil;
                updated.RepeatUntilExact = edited.AllDay == master.AllDay &&
                    edited.RepeatUntil?.Date == master.RepeatUntil?.Date ? master.RepeatUntilExact : null;
                updated.RepeatCount    = edited.RepeatCount;
                updated.Modified       = DateTime.UtcNow;

                // Recover each deleted occurrence's time before shifting its date. A move
                // across midnight can change the day even when delta is less than 24 hours.
                updated.SkipDates = master.SkipDates
                    .Select(day => (day.Date + master.Start.TimeOfDay + delta).Date)
                    .Where(day => Recurrence.GeneratesDate(updated, day))
                    .ToList();
                updated.SkipOccurrences = [];
                foreach (var original in master.SkipOccurrences)
                {
                    if (updated.AllDay)
                    {
                        var local = Recurrence.UsesTimeBasis(master)
                            ? Recurrence.FromTimeBasis(master, original).ToLocalTime() : original;
                        var day = (local + delta).Date;
                        if (Recurrence.GeneratesDate(updated, day) && !updated.SkipDates.Contains(day))
                            updated.SkipDates.Add(day);
                    }
                    else
                    {
                        var shifted = original + nominalDelta;
                        if (Recurrence.GeneratesCanonicalStart(updated, shifted)) updated.SkipOccurrences.Add(shifted);
                    }
                }

                var retained = new List<CalendarEvent>();
                var removed = new HashSet<Guid>();
                foreach (var exception in _events.Where(e => e.SeriesId == master.Id))
                {
                    if (!exception.OccurrenceStart.HasValue)
                    {
                        removed.Add(exception.Id);
                        continue;
                    }
                    var oldNominal = Recurrence.CanonicalOccurrenceStart(master, exception);
                    var identity = master.AllDay != updated.AllDay
                        ? (exception.OccurrenceStart.Value + delta).Date + updated.Start.TimeOfDay
                        : oldNominal + nominalDelta;
                    if (updated.AllDay) identity = identity.Date;
                    if (!Recurrence.GeneratesCanonicalStart(updated, identity))
                    {
                        removed.Add(exception.Id);
                        continue;
                    }
                    var shifted = exception.Clone();
                    Recurrence.SetOccurrenceIdentity(shifted, updated, identity);
                    if (Recurrence.UsesTimeBasis(master) && Recurrence.UsesTimeBasis(updated))
                    {
                        var physicalDelta = Recurrence.OriginalInstant(shifted) - Recurrence.OriginalInstant(exception);
                        shifted.Start = (exception.Start.ToUniversalTime() + physicalDelta).ToLocalTime();
                        shifted.End = (exception.End.ToUniversalTime() + physicalDelta).ToLocalTime();
                    }
                    else
                    {
                        shifted.Start += delta;
                        shifted.End += delta;
                    }
                    shifted.Modified = updated.Modified;
                    if (NominalDuration.HasValue(shifted))
                        shifted.End = NominalDuration.EndAfterStartChange(shifted, shifted.Start);
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
            Commit(() =>
            {
                var master = GetSeriesMaster(edited);
                if (master == null) throw new InvalidOperationException("The recurring appointment could not be found.");
                var row = edited.Clone();
                var nominal = Recurrence.CanonicalOccurrenceStart(master, edited);
                Recurrence.SetOccurrenceIdentity(row, master, nominal);
                row.SeriesId = master.Id;
                long identity = Recurrence.OccurrenceKey(master, row);
                var existing = _events.FirstOrDefault(e => e.IsOverride && e.SeriesId == master.Id &&
                    e.OccurrenceStart.HasValue && Recurrence.OccurrenceKey(master, e) == identity);
                row.Id = existing?.Id ?? Guid.NewGuid();
                row.Created = existing?.Created ?? DateTime.UtcNow;
                row.Modified = DateTime.UtcNow;
                row.Repeat = RepeatFreq.None;
                row.RepeatInterval = 1;
                row.RepeatDays = [];
                row.RepeatUntil = null;
                row.RepeatUntilExact = null;
                row.RepeatCount = 0;
                row.SkipDates = [];
                row.SkipOccurrences = [];
                Upsert(row);
                if (existing == null) _events.Add(row);
                else _events[_events.FindIndex(e => e.Id == existing.Id)] = row;
            });
        }

        /// <summary>Removes ONE date from a series, leaving the rest of it alone.</summary>
        public void DeleteOccurrence(CalendarEvent occ)
        {
            var master = GetSeriesMaster(occ);
            if (master == null) return;
            Commit(() =>
            {
                var nominal = Recurrence.CanonicalOccurrenceStart(master, occ);
                long identity = Recurrence.OccurrenceKey(master, occ);
                var stray = _events.FirstOrDefault(e => e.IsOverride && e.SeriesId == master.Id &&
                    e.OccurrenceStart.HasValue && Recurrence.OccurrenceKey(master, e) == identity);
                var updated = master.Clone();
                if (master.AllDay)
                {
                    if (!updated.SkipDates.Any(day => day.Date == nominal.Date)) updated.SkipDates.Add(nominal.Date);
                }
                else if (!updated.SkipOccurrences.Any(value => value.Ticks == nominal.Ticks))
                    updated.SkipOccurrences.Add(nominal);
                updated.Modified = DateTime.UtcNow;
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
