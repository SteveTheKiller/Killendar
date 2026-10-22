using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>The new appointments of an import, and how many were already present.</summary>
    public sealed class ImportPlan
    {
        public List<CalendarEvent> Fresh { get; } = [];
        public int AlreadyPresent { get; internal set; }

        /// <summary>Replacement rows for appointments already here: a newer invite's version, or a
        /// series with a date the organizer canceled.</summary>
        public List<CalendarEvent> Updated { get; } = [];

        /// <summary>Rows the organizer canceled, with a canceled series' one-date edits.</summary>
        public List<CalendarEvent> Removed { get; } = [];

        /// <summary>The canceled appointments, each with its own one-date edits, as each goes to
        /// Recently deleted: one entry per meeting, as deleting it by hand gives.</summary>
        internal List<List<CalendarEvent>> Trashed { get; } = [];

        /// <summary>How many appointments a newer invite changed, and how many the organizer
        /// canceled (whole appointments and single dates).</summary>
        public int NewerInvites { get; internal set; }
        public int Cancellations { get; internal set; }

        /// <summary>True when the import changes or removes something already in the Killendar.</summary>
        public bool ChangesExisting => Updated.Count + Removed.Count > 0;

        /// <summary>True when there is anything at all to write.</summary>
        public bool HasWork => Fresh.Count > 0 || ChangesExisting;
    }

    public partial class EventStore
    {
        // ============================================================
        // Categories (per-Killendar definitions; assignment is the events.categories string,
        // so there is no join table to keep in step and an event carries its own list)
        // ============================================================

        /// <summary>Splits an assignment string into category names, trimmed, blanks dropped.</summary>
        public static List<string> SplitCategories(string categories) =>
            string.IsNullOrEmpty(categories)
                ? []
                : [.. categories.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0)];

        /// <summary>Canonical storage form: trimmed names, ", " separated, duplicates dropped.</summary>
        public static string NormalizeCategories(string? categories)
        {
            if (string.IsNullOrWhiteSpace(categories)) return "";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<string>();
            foreach (string c in SplitCategories(categories!))
                if (seen.Add(c)) kept.Add(c);
            return string.Join(", ", kept);
        }

        internal static string WithPrimaryCategory(string categories, string primary)
        {
            var names = SplitCategories(NormalizeCategories(categories));
            string name = names.FirstOrDefault(n => string.Equals(n, primary, StringComparison.OrdinalIgnoreCase))
                          ?? primary.Trim();
            if (string.IsNullOrWhiteSpace(name)) return NormalizeCategories(categories);
            names.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            names.Insert(0, name);
            return string.Join(", ", names);
        }

        /// <summary>Definitions in insertion order, the order the pickers show them in.</summary>
        public List<(string Name, string Color)> ListCategories()
        {
            var list = new List<(string, string)>();
            if (_db == null) return list;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT name, color FROM categories ORDER BY rowid";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
            return list;
        }

        /// <summary>Adds a definition; an existing name (case-insensitive) wins. False when the
        /// write failed, with the reason in LoadError.</summary>
        public bool AddCategory(string name, string color)
        {
            if (_db == null) return false;
            // Commit refreshes the paint cache BEFORE the repaint, or the views redraw from the
            // colors that were just replaced. Seeding uses the static overload, so this cannot
            // fire mid-Open.
            return Commit(() => AddCategory(_db, name, color), refreshCategories: true);
        }

        private static void AddCategory(SqliteConnection db, string name, string color)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO categories(name, color) VALUES ($n, $c)";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$c", color);
            cmd.ExecuteNonQuery();
        }

        /// <summary>False when the write failed; the paint cache then holds the stored color again,
        /// which also discards any live preview.</summary>
        public bool SetCategoryColor(string name, string color)
        {
            if (_db == null) return false;
            // Assignment strings hold names, not colors, so nothing to rewrite - but the views
            // paint from the definitions, so they still need telling.
            return Commit(() =>
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "UPDATE categories SET color = $c WHERE name = $n";
                cmd.Parameters.AddWithValue("$c", color);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }, refreshCategories: true);
        }

        /// <summary>Renames a definition and rewrites it inside every event's assignment.</summary>
        public void RenameCategory(string oldName, string newName)
        {
            RewriteCategoryInEvents(oldName, newName, () =>
            {
                using var cmd = _db!.CreateCommand();
                cmd.CommandText = "UPDATE categories SET name = $new WHERE name = $old";
                cmd.Parameters.AddWithValue("$new", newName);
                cmd.Parameters.AddWithValue("$old", oldName);
                cmd.ExecuteNonQuery();
            });
        }

        /// <summary>Deletes a definition and removes it from every event's assignment.</summary>
        public void DeleteCategory(string name)
        {
            RewriteCategoryInEvents(name, null, () =>
            {
                using var cmd = _db!.CreateCommand();
                cmd.CommandText = "DELETE FROM categories WHERE name = $n";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            });
        }

        // Renames (newName != null) or removes (null) one category across every event. The
        // definition and affected events share one transaction. Modified is left alone:
        // renaming a definition is not an edit to the appointment.
        private void RewriteCategoryInEvents(string name, string? newName, Action writeDefinition)
        {
            var touched = new List<CalendarEvent>();
            foreach (var ev in _events)
            {
                bool hit = false;
                var kept = new List<string>();
                foreach (string c in SplitCategories(ev.Categories))
                {
                    if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = true;
                        if (newName != null && !kept.Contains(newName, StringComparer.OrdinalIgnoreCase))
                            kept.Add(newName);
                    }
                    else kept.Add(c);
                }
                if (!hit) continue;
                var updated = ev.Clone();
                updated.Categories = string.Join(", ", kept);
                touched.Add(updated);
            }
            Commit(() =>
            {
                using var tx = _db!.BeginTransaction();
                writeDefinition();
                foreach (var ev in touched) Upsert(ev);
                tx.Commit();
                foreach (var ev in touched) _events[_events.FindIndex(e => e.Id == ev.Id)] = ev;
            }, refreshCategories: true);
        }

        /// <summary>Import events, skipping any whose Id is already present. Returns how many were
        /// added. One transaction, because a 2000-event .ics one INSERT at a time is slow enough
        /// to be visible. The interactive import uses the three steps below separately so the
        /// write can run away from the window.</summary>
        public int ImportEvents(IEnumerable<CalendarEvent> incoming) => Recorded(() =>
        {
            var plan = PlanImport(incoming);
            if (!plan.HasWork) return 0;

            BackupNow(DateTime.Now);
            bool saved = Commit(() => WriteRows(plan, CancellationToken.None, null));
            if (saved) Show(plan);
            return saved ? plan.Fresh.Count : 0;
        });

        /// <summary>
        /// What an import does to the Killendar. Reads only the in-memory mirror.
        ///   - an appointment not here yet is added;
        ///   - one already here comes in again: a newer invite (a higher SEQUENCE, which organizers
        ///     raise when they change a meeting) replaces the organizer's details, keeping the
        ///     categories and reminder chosen here; the same or an older one is skipped;
        ///   - a canceled appointment or a canceled date of a series here is removed, the
        ///     appointment to Recently deleted.
        /// </summary>
        public ImportPlan PlanImport(IEnumerable<CalendarEvent> incoming,
                                     IEnumerable<CalendarEvent>? canceled = null,
                                     IEnumerable<CalendarEvent>? canceledOccurrences = null)
        {
            var plan = new ImportPlan();
            var byId = new Dictionary<Guid, CalendarEvent>();
            foreach (var ev in _events) byId[ev.Id] = ev;
            var seen = new HashSet<Guid>();
            var replaced = new Dictionary<Guid, CalendarEvent>();
            var incomingList = incoming.ToList();
            var incomingIds = new HashSet<Guid>(incomingList.Select(e => e.Id));
            var removed = new HashSet<Guid>();

            foreach (var ev in incomingList)
            {
                if (!seen.Add(ev.Id)) { plan.AlreadyPresent++; continue; }
                if (!byId.TryGetValue(ev.Id, out var have)) { plan.Fresh.Add(ev); continue; }
                if (MeetingInfo.Sequence(ev) <= MeetingInfo.Sequence(have)) { plan.AlreadyPresent++; continue; }
                var update = ev.Clone();
                update.Categories = have.Categories;          // chosen here, not the organizer's
                update.ReminderMinutes = have.ReminderMinutes;
                update.Created = have.Created;
                update.Modified = DateTime.UtcNow;
                if (have.IsSeries && update.IsSeries) KeepWhatStillFits(have, update, incomingIds, plan, removed);
                replaced[update.Id] = update;
                plan.NewerInvites++;
            }

            foreach (var cancel in canceled ?? [])
            {
                var id = cancel.Id;
                if (!byId.TryGetValue(id, out var have) || removed.Contains(id)) continue;
                // An older cancellation (from before the meeting was put back) does not remove it.
                if (MeetingInfo.Sequence(cancel) < MeetingInfo.Sequence(replaced.TryGetValue(id, out var newer0) ? newer0 : have)) continue;
                var group = new List<CalendarEvent>();
                foreach (var row in _events.Where(e => e.Id == id || e.SeriesId == id))
                    if (removed.Add(row.Id)) { plan.Removed.Add(row); group.Add(row); }
                if (group.Count > 0) plan.Trashed.Add(group);
                replaced.Remove(id);
                plan.Fresh.RemoveAll(e => e.Id == id || e.SeriesId == id);
                plan.Cancellations++;
            }

            foreach (var occ in canceledOccurrences ?? [])
            {
                if (occ.SeriesId is not Guid seriesId || removed.Contains(seriesId)) continue;
                var master = replaced.TryGetValue(seriesId, out var newer) ? newer
                           : byId.TryGetValue(seriesId, out var here) && here.IsSeries ? here : null;
                if (master == null || !master.IsSeries) continue;
                if (MeetingInfo.Sequence(occ) < MeetingInfo.Sequence(master)) continue;   // older than the series here
                var nominal = Recurrence.CanonicalOccurrenceStart(master, occ);
                var updated = replaced.TryGetValue(seriesId, out var pending) ? pending : master.Clone();
                if (master.AllDay)
                {
                    if (updated.SkipDates.Any(d => d.Date == nominal.Date)) continue;
                    updated.SkipDates.Add(nominal.Date);
                }
                else
                {
                    if (updated.SkipOccurrences.Any(v => v.Ticks == nominal.Ticks)) continue;
                    updated.SkipOccurrences.Add(nominal);
                }
                updated.Modified = DateTime.UtcNow;
                replaced[seriesId] = updated;
                long identity = Recurrence.OccurrenceKey(master, occ);
                foreach (var edit in _events.Where(e => e.IsOverride && e.SeriesId == seriesId && e.OccurrenceStart.HasValue &&
                                                        Recurrence.OccurrenceKey(master, e) == identity))
                    if (removed.Add(edit.Id)) plan.Removed.Add(edit);
                plan.Cancellations++;
            }

            plan.Updated.AddRange(replaced.Values);
            return plan;
        }

        /// <summary>
        /// A newer invite for a series replaces the organizer's pattern; what was done here to its
        /// dates is kept where it still fits. A date skipped here stays skipped while the new
        /// pattern still gives it. When the pattern or its time changed, the one-date edits made
        /// here no longer name a date of it, so they go (the file's own edits come with it).
        /// </summary>
        private void KeepWhatStillFits(CalendarEvent have, CalendarEvent update, HashSet<Guid> incomingIds,
                                       ImportPlan plan, HashSet<Guid> removed)
        {
            bool samePattern = update.Start == have.Start && update.AllDay == have.AllDay && update.Repeat == have.Repeat &&
                update.RepeatInterval == have.RepeatInterval && update.RepeatMonthWeek == have.RepeatMonthWeek &&
                (update.RepeatDays ?? []).OrderBy(d => d).SequenceEqual((have.RepeatDays ?? []).OrderBy(d => d));
            foreach (var day in have.SkipDates)
                if (!update.SkipDates.Any(d => d.Date == day.Date) && Recurrence.GeneratesDate(update, day)) update.SkipDates.Add(day.Date);
            if (samePattern)
            {
                foreach (var nominal in have.SkipOccurrences)
                    if (!update.SkipOccurrences.Any(v => v.Ticks == nominal.Ticks)) update.SkipOccurrences.Add(nominal);
                update.ExtraDates = [.. update.ExtraDates.Union(have.ExtraDates.Select(d => d.Date))];
                update.RepeatShift = have.RepeatShift;
                return;
            }
            foreach (var edit in _events.Where(e => e.IsOverride && e.SeriesId == have.Id && !incomingIds.Contains(e.Id)))
                if (removed.Add(edit.Id)) plan.Removed.Add(edit);
        }

        /// <summary>Puts a written import into the mirror.</summary>
        private void Show(ImportPlan plan)
        {
            var gone = new HashSet<Guid>(plan.Removed.Select(e => e.Id));
            _events.RemoveAll(e => gone.Contains(e.Id));
            foreach (var row in plan.Updated)
            {
                int i = _events.FindIndex(e => e.Id == row.Id);
                if (i >= 0) _events[i] = row; else _events.Add(row);
            }
            _events.AddRange(plan.Fresh);
        }

        /// <summary>True while WriteImport runs. Anything else that would use the database on
        /// another thread - the command pipe - must refuse rather than share the connection.</summary>
        public bool IsImporting => _importing;
        private volatile bool _importing;
        private readonly object _writeGate = new();

        /// <summary>
        /// Writes a planned import in one transaction WITHOUT touching the in-memory mirror or
        /// raising Changed, so it may run on a worker thread while the window waits. Cancellation is
        /// checked between rows and once more before the commit: a canceled import rolls back and
        /// writes nothing. Follow with PublishImport on the window's thread.
        /// </summary>
        public void WriteImport(ImportPlan plan, CancellationToken cancel, Action? afterRow = null)
        {
            if (_db == null) throw new InvalidOperationException("No Killendar is open.");
            lock (_writeGate)
            {
                _importing = true;
                BackupNow(DateTime.Now);
                try { WriteRows(plan, cancel, afterRow); }
                finally { _importing = false; }
            }
        }

        private void WriteRows(ImportPlan plan, CancellationToken cancel, Action? afterRow)
        {
            using var tx = _db!.BeginTransaction();
            using var upsert = CreateUpsert(_db);
            foreach (var ev in plan.Fresh.Concat(plan.Updated))
            {
                cancel.ThrowIfCancellationRequested();
                BindUpsert(upsert, ev);
                upsert.ExecuteNonQuery();
                afterRow?.Invoke();
            }
            if (plan.Removed.Count > 0)
            {
                // Each canceled meeting goes to Recently deleted on its own, as a delete by hand
                // would. A one-date edit dropped with its date is just removed.
                foreach (var group in plan.Trashed) Trash(group);
                foreach (var row in plan.Removed)
                {
                    using var delete = _db.CreateCommand();
                    delete.CommandText = "DELETE FROM events WHERE id = $id";
                    delete.Parameters.AddWithValue("$id", row.Id.ToString());
                    delete.ExecuteNonQuery();
                }
            }
            cancel.ThrowIfCancellationRequested();
            tx.Commit();
        }

        /// <summary>Shows a written import: adds it to the mirror and repaints once.</summary>
        public int PublishImport(ImportPlan plan) => Recorded(() =>
        {
            Show(plan);
            LoadError = null;
            Changed?.Invoke();
            return plan.Fresh.Count;
        });
    }
}
