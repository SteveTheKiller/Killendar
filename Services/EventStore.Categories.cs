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
        public int ImportEvents(IEnumerable<CalendarEvent> incoming)
        {
            var plan = PlanImport(incoming);
            if (plan.Fresh.Count == 0) return 0;

            _events.AddRange(plan.Fresh);
            bool saved = Commit(() => WriteRows(plan.Fresh, CancellationToken.None, null));
            return saved ? plan.Fresh.Count : 0;
        }

        /// <summary>Which incoming appointments are new. Reads only the in-memory mirror.</summary>
        public ImportPlan PlanImport(IEnumerable<CalendarEvent> incoming)
        {
            var plan = new ImportPlan();
            var have = new HashSet<Guid>(_events.Select(e => e.Id));
            foreach (var ev in incoming)
            {
                if (have.Add(ev.Id)) plan.Fresh.Add(ev);
                else plan.AlreadyPresent++;
            }
            return plan;
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
                try { WriteRows(plan.Fresh, cancel, afterRow); }
                finally { _importing = false; }
            }
        }

        private void WriteRows(List<CalendarEvent> rows, CancellationToken cancel, Action? afterRow)
        {
            using var tx = _db!.BeginTransaction();
            foreach (var ev in rows)
            {
                cancel.ThrowIfCancellationRequested();
                Upsert(ev);
                afterRow?.Invoke();
            }
            cancel.ThrowIfCancellationRequested();
            tx.Commit();
        }

        /// <summary>Shows a written import: adds it to the mirror and repaints once.</summary>
        public int PublishImport(ImportPlan plan)
        {
            _events.AddRange(plan.Fresh);
            LoadError = null;
            Changed?.Invoke();
            return plan.Fresh.Count;
        }
    }
}
