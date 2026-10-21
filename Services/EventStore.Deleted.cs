using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>One deletion that can still be put back.</summary>
    public sealed class DeletedItem
    {
        public Guid Id { get; internal set; }
        public DateTime DeletedUtc { get; internal set; }
        public string Title { get; internal set; } = "";
        public DateTime Start { get; internal set; }
        /// <summary>Rows the deletion removed: 1 for an appointment, more for a series with
        /// single-date edits.</summary>
        public int Rows { get; internal set; }
    }

    public partial class EventStore
    {
        // ============================================================
        // Recently deleted. Deleting an appointment or a whole series also files its rows in the
        // `deleted` table of the same .kcal, so they are encrypted with the calendar and travel
        // with it. Undo covers the session; this covers the next 30 days and a restart. Removing
        // one date of a series is not a deletion here: it only adds a skip to the series, which
        // undo reverses.
        // ============================================================

        internal static readonly TimeSpan DeletedRetention = TimeSpan.FromDays(30);

        private static readonly JsonSerializerOptions DeletedJson = new() { IgnoreReadOnlyProperties = true };

        /// <summary>Files rows in the trash. Called inside the deleting transaction, so the two
        /// can never disagree.</summary>
        private void Trash(List<CalendarEvent> rows)
        {
            if (rows.Count == 0) return;
            var head = rows.FirstOrDefault(r => !r.IsOverride) ?? rows[0];
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "INSERT INTO deleted(id, deleted_utc, title, start_utc, rows) VALUES ($id, $at, $ti, $st, $rows)";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$ti", head.Title ?? "");
            cmd.Parameters.AddWithValue("$st", head.Start.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$rows", JsonSerializer.Serialize(rows, DeletedJson));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Newest first. A deletion whose appointment is already back (undone) is not
        /// listed: restoring it would add nothing.</summary>
        public List<DeletedItem> ListDeleted()
        {
            var list = new List<DeletedItem>();
            if (_db == null || !TableExists("deleted")) return list;
            var present = new HashSet<Guid>(_events.Select(e => e.Id));
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT id, deleted_utc, title, start_utc, rows FROM deleted ORDER BY deleted_utc DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var rows = Rows(r.GetString(4));
                if (rows.Count == 0 || rows.All(row => present.Contains(row.Id))) continue;
                list.Add(new DeletedItem
                {
                    Id = Guid.Parse(r.GetString(0)),
                    DeletedUtc = DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    Title = r.GetString(2),
                    Start = DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime(),
                    Rows = rows.Count,
                });
            }
            return list;
        }

        /// <summary>Puts a deletion's rows back, skipping any already present, and removes the
        /// entry. It is an ordinary change, so Ctrl+Z takes it away again.</summary>
        public bool RestoreDeleted(Guid id) => Recorded(() =>
        {
            if (_db == null) return false;
            List<CalendarEvent> rows;
            using (var read = _db.CreateCommand())
            {
                read.CommandText = "SELECT rows FROM deleted WHERE id = $id";
                read.Parameters.AddWithValue("$id", id.ToString());
                if (read.ExecuteScalar() is not string json) return false;
                rows = Rows(json);
            }
            var present = new HashSet<Guid>(_events.Select(e => e.Id));
            var back = rows.Where(row => !present.Contains(row.Id)).ToList();
            return Commit(() =>
            {
                using (var tx = _db.BeginTransaction())
                {
                    foreach (var row in back) Upsert(row);
                    using var remove = _db.CreateCommand();
                    remove.CommandText = "DELETE FROM deleted WHERE id = $id";
                    remove.Parameters.AddWithValue("$id", id.ToString());
                    remove.ExecuteNonQuery();
                    tx.Commit();
                }
                _events.AddRange(back);
            });
        });

        /// <summary>Drops entries past the retention period. Runs when a calendar opens.</summary>
        internal void PurgeDeleted(DateTime nowUtc)
        {
            if (_db == null || !TableExists("deleted")) return;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM deleted WHERE deleted_utc < $cut";
            cmd.Parameters.AddWithValue("$cut", (nowUtc - DeletedRetention).ToString("o", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }

        private static List<CalendarEvent> Rows(string json)
        {
            try { return JsonSerializer.Deserialize<List<CalendarEvent>>(json, DeletedJson) ?? []; }
            catch (JsonException) { return []; }
        }
    }
}
