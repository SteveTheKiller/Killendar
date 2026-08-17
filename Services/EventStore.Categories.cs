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

        /// <summary>Adds a definition; an existing name (case-insensitive) wins.</summary>
        public void AddCategory(string name, string color)
        {
            if (_db == null) return;
            AddCategory(_db, name, color);
            AfterDefinitionChange();
        }

        // Every definition edit has to refresh the paint cache BEFORE the repaint, or the views
        // redraw from the colors that were just replaced. Seeding does not come through here - it
        // uses the static overload - so this cannot fire mid-Open.
        private void AfterDefinitionChange()
        {
            CategoryManager.Refresh(this);
            Changed?.Invoke();
        }

        private static void AddCategory(SqliteConnection db, string name, string color)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO categories(name, color) VALUES ($n, $c)";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$c", color);
            cmd.ExecuteNonQuery();
        }

        public void SetCategoryColor(string name, string color)
        {
            if (_db == null) return;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "UPDATE categories SET color = $c WHERE name = $n";
                cmd.Parameters.AddWithValue("$c", color);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }
            // Assignment strings hold names, not colors, so nothing to rewrite - but the views
            // paint from the definitions, so they still need telling.
            AfterDefinitionChange();
        }

        /// <summary>Renames a definition and rewrites it inside every event's assignment.</summary>
        public void RenameCategory(string oldName, string newName)
        {
            if (_db == null) return;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "UPDATE categories SET name = $new WHERE name = $old";
                cmd.Parameters.AddWithValue("$new", newName);
                cmd.Parameters.AddWithValue("$old", oldName);
                cmd.ExecuteNonQuery();
            }
            CategoryManager.Refresh(this);
            RewriteCategoryInEvents(oldName, newName);
        }

        /// <summary>Deletes a definition and removes it from every event's assignment.</summary>
        public void DeleteCategory(string name)
        {
            if (_db == null) return;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM categories WHERE name = $n";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }
            CategoryManager.Refresh(this);
            RewriteCategoryInEvents(name, null);
        }

        // Renames (newName != null) or removes (null) one category across every event. The
        // in-memory mirror is rewritten first because reads are served from it, then only the
        // events that actually changed are written back. Modified is deliberately left alone:
        // renaming a definition is not an edit to the appointment.
        private void RewriteCategoryInEvents(string name, string? newName)
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
                ev.Categories = string.Join(", ", kept);
                touched.Add(ev);
            }
            Commit(() =>
            {
                foreach (var ev in touched) Upsert(ev);
            });
        }

        /// <summary>Import events, skipping any whose Id is already present. Returns how many were
        /// added. One transaction, because a 2000-event .ics one INSERT at a time is slow enough
        /// to be visible.</summary>
        public int ImportEvents(IEnumerable<CalendarEvent> incoming)
        {
            var fresh = new List<CalendarEvent>();
            var have = new HashSet<Guid>(_events.Select(e => e.Id));
            foreach (var ev in incoming)
            {
                if (!have.Add(ev.Id)) continue;
                fresh.Add(ev);
            }
            if (fresh.Count == 0) return 0;

            _events.AddRange(fresh);
            Commit(() =>
            {
                using var tx = _db!.BeginTransaction();
                foreach (var ev in fresh) Upsert(ev);
                tx.Commit();
            });
            return fresh.Count;
        }
    }
}
