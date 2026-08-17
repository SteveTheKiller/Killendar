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
        // Schema
        // ============================================================

        // events_range covers GetInRange, which is the only hot query - every view repaint calls
        // it. Reads currently come from the in-memory mirror, so the index is insurance for the
        // day a query goes back to SQL, and it costs one B-tree on a table of a few thousand rows.
        private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS events (
    id           TEXT PRIMARY KEY,
    title        TEXT NOT NULL DEFAULT '',
    start_utc    TEXT NOT NULL,
    end_utc      TEXT NOT NULL,
    all_day      INTEGER NOT NULL DEFAULT 0,
    location     TEXT NOT NULL DEFAULT '',
    description  TEXT NOT NULL DEFAULT '',
    attendees    TEXT NOT NULL DEFAULT '',
    categories   TEXT NOT NULL DEFAULT '',
    created_utc  TEXT NOT NULL,
    modified_utc TEXT NOT NULL,
    repeat_freq      INTEGER NOT NULL DEFAULT 0,
    repeat_interval  INTEGER NOT NULL DEFAULT 1,
    repeat_days      TEXT    NOT NULL DEFAULT '',
    repeat_until     TEXT    NOT NULL DEFAULT '',
    repeat_count     INTEGER NOT NULL DEFAULT 0,
    skip_dates       TEXT    NOT NULL DEFAULT '',
    series_id        TEXT    NOT NULL DEFAULT '',
    occurrence_start TEXT    NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS events_range ON events (start_utc, end_utc);
CREATE TABLE IF NOT EXISTS categories (
    name  TEXT PRIMARY KEY COLLATE NOCASE,
    color TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);";

        private void EnsureSchema()
        {
            // Captured BEFORE the CREATE, so the seed below can tell a brand new Killendar from
            // one whose categories the user has already emptied out.
            bool hadCategories = TableExists("categories");
            Exec(SchemaSql);
            EnsureColumns();
            // Seed ONLY when the categories table was just created: customizations and
            // deletions must never resurrect (KillerNotes tags design, same rule).
            if (!hadCategories) SeedDefaultCategories();
            SetMeta("schema_version", SchemaVersion.ToString());
            // Stamped once, for forensics on a file that turns up years later. Read straight off
            // the assembly rather than through About.cs, whose version helper is private to
            // MainWindow.
            if (GetMeta("app_version_created") == null)
                SetMeta("app_version_created",
                    typeof(EventStore).Assembly.GetName().Version?.ToString() ?? "0");
        }

        private bool TableExists(string name)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name = $n";
            cmd.Parameters.AddWithValue("$n", name);
            return (long)cmd.ExecuteScalar()! > 0;
        }

        // Outlook-style starter set in the family palette (the same hexes as the accent row).
        private static readonly (string Name, string Color)[] DefaultCategories =
        [
            ("Red",   "#DD504B"), ("Orange", "#E8962C"), ("Yellow", "#E8D44B"),
            ("Green", "#1EA54C"), ("Blue",   "#50AEE8"), ("Purple", "#B982E3"),
        ];

        private void SeedDefaultCategories()
        {
            if (_db != null) SeedDefaultCategories(_db);
        }

        // Takes the connection explicitly for the same reason Upsert does: the events.json
        // migration builds a database that is not the open one, and it runs SchemaSql itself,
        // so it must seed there or EnsureSchema will later see the table already present and
        // skip the seed - leaving a migrated Killendar with no categories at all.
        private static void SeedDefaultCategories(SqliteConnection db)
        {
            foreach (var (name, color) in DefaultCategories) AddCategory(db, name, color);
        }

        /// <summary>Additive columns from v2 and v3. ALTER-on-open is needed because CREATE TABLE
        /// IF NOT EXISTS never touches an existing table, so a Killendar written by 1.0.0 keeps its
        /// original events table; PRAGMA-checking first keeps this idempotent and cheap.
        ///
        /// Every column added here MUST have a default that reads back as "not repeating", so an
        /// older Killendar opens with its appointments unchanged rather than acquiring repeats.
        /// </summary>
        private void EnsureColumns()
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = _db!.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(events)";
                using var r = cmd.ExecuteReader();
                while (r.Read()) have.Add(r.GetString(1));
            }

            void AddColumn(string name, string decl)
            {
                if (!have.Contains(name))
                    Exec("ALTER TABLE events ADD COLUMN " + name + " " + decl);
            }

            AddColumn("categories",       "TEXT NOT NULL DEFAULT ''");
            AddColumn("repeat_freq",      "INTEGER NOT NULL DEFAULT 0");
            AddColumn("repeat_interval",  "INTEGER NOT NULL DEFAULT 1");
            AddColumn("repeat_days",      "TEXT NOT NULL DEFAULT ''");
            AddColumn("repeat_until",     "TEXT NOT NULL DEFAULT ''");
            AddColumn("repeat_count",     "INTEGER NOT NULL DEFAULT 0");
            AddColumn("skip_dates",       "TEXT NOT NULL DEFAULT ''");
            AddColumn("series_id",        "TEXT NOT NULL DEFAULT ''");
            AddColumn("occurrence_start", "TEXT NOT NULL DEFAULT ''");
        }

        private string? GetMeta(string key)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }

        private void SetMeta(string key, string value)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = "INSERT INTO meta(key, value) VALUES($k, $v) " +
                              "ON CONFLICT(key) DO UPDATE SET value = $v";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        private void Exec(string sql)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // Row mapping. UTC on disk, local in memory - see the class remarks.
        // ============================================================

        private const string RoundTrip = "yyyy-MM-ddTHH:mm:ss.fffffffK";

        /// <summary>Formats an appointment boundary for storage. All-day events are floating
        /// calendar dates and MUST NOT be shifted into UTC.</summary>
        private static string ToStore(DateTime local, bool allDay) => allDay
            ? DateTime.SpecifyKind(local, DateTimeKind.Unspecified).ToString(RoundTrip)
            : local.ToUniversalTime().ToString(RoundTrip);

        private static DateTime FromStore(string s, bool allDay)
        {
            var styles = allDay
                ? System.Globalization.DateTimeStyles.None
                : System.Globalization.DateTimeStyles.AdjustToUniversal |
                  System.Globalization.DateTimeStyles.AssumeUniversal;
            if (!DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, styles, out var dt))
                return default;
            if (allDay) return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
        }

        // Created and Modified are already UTC in the model, so they are stored and read straight
        // through with no zone conversion.
        private static string ToStoreUtc(DateTime utc) =>
            (utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc).ToString(RoundTrip);

        private static DateTime FromStoreUtc(string s) =>
            DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.UtcNow;

        // Attendees are newline separated. A newline cannot appear in an ICS ATTENDEE value, so
        // there is nothing to escape and nothing round-trips wrong.
        private static string JoinAttendees(IEnumerable<string> a) =>
            string.Join("\n", a.Where(x => !string.IsNullOrWhiteSpace(x)));

        private static List<string> SplitAttendees(string s) =>
            string.IsNullOrEmpty(s)
                ? []
                : [.. s.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x))];

        // ── Repeat columns ────────────────────────────────────────────────────────────────
        // Weekdays and skipped dates are short comma-separated lists rather than side tables:
        // they belong to exactly one appointment, are read whole every time, and are never
        // queried across events. Same reasoning as the categories assignment string.

        private const string DateOnly = "yyyy-MM-dd";

        private static string JoinDays(IEnumerable<DayOfWeek> days) =>
            string.Join(",", days.Select(d => ((int)d).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        private static List<DayOfWeek> SplitDays(string s)
        {
            var list = new List<DayOfWeek>();
            if (string.IsNullOrWhiteSpace(s)) return list;
            foreach (var part in s.Split(','))
            {
                if (int.TryParse(part.Trim(), out int n) && n >= 0 && n <= 6 &&
                    !list.Contains((DayOfWeek)n))
                    list.Add((DayOfWeek)n);
            }
            list.Sort();
            return list;
        }

        // Skipped dates and the until date are calendar DATES, not instants: they are floating,
        // exactly like an all-day event, and must never be shifted into UTC or a user east of
        // Greenwich loses the wrong day.
        private static string JoinDates(IEnumerable<DateTime> dates) =>
            string.Join(",", dates.Select(d => d.Date.ToString(DateOnly,
                System.Globalization.CultureInfo.InvariantCulture)));

        private static List<DateTime> SplitDates(string s)
        {
            var list = new List<DateTime>();
            if (string.IsNullOrWhiteSpace(s)) return list;
            foreach (var part in s.Split(','))
            {
                if (DateTime.TryParseExact(part.Trim(), DateOnly,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var d))
                    list.Add(d.Date);
            }
            return list;
        }

        private static string ToStoreDate(DateTime? d) => d.HasValue
            ? d.Value.Date.ToString(DateOnly, System.Globalization.CultureInfo.InvariantCulture)
            : "";

        private static DateTime? FromStoreDate(string s) =>
            DateTime.TryParseExact(s, DateOnly, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d)
                ? d.Date : (DateTime?)null;

        /// <summary>An unknown number reads as "does not repeat". A Killendar written by a LATER
        /// Killendar that has more patterns must not throw here: the appointment still shows, it
        /// just shows once, which is the same honest degradation the .ics importer makes.</summary>
        private static RepeatFreq ToFreq(long n) =>
            n >= (long)RepeatFreq.None && n <= (long)RepeatFreq.Yearly
                ? (RepeatFreq)n : RepeatFreq.None;

        private static Guid? ParseGuid(string s) =>
            Guid.TryParse(s, out var g) ? g : (Guid?)null;

        /// <summary>Empty column reads as null rather than 0001-01-01.</summary>
        private static DateTime? FromStoreNullable(string s, bool allDay) =>
            string.IsNullOrWhiteSpace(s) ? null : FromStore(s, allDay);
    }
}
