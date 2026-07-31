using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// Every appointment in the open Killendar. The durable store is a SQLite database - one
    /// .kcal file is one Killendar - optionally encrypted at rest with SQLCipher (see
    /// SqlCipherBootstrap). Reads are served from an in-memory mirror rather than SQL, because
    /// every view repaint calls GetInRange or GetOnDay and a heavy calendar is a few thousand
    /// events: cheap to hold, and it keeps the query semantics the views already depend on
    /// identical to the JSON implementation this replaces.
    ///
    /// Changed fires after any mutation so the views can repaint without polling.
    ///
    /// TIME HANDLING, the one thing to get right: in memory Start and End are LOCAL, because the
    /// views format them directly. On disk they are UTC. All-day events are the exception - they
    /// are floating calendar dates and are stored unconverted, or an all-day appointment slides
    /// to the previous day for everyone east of UTC.
    /// </summary>
    public class EventStore
    {
        // The provider installs exactly once per process, before the first open, and the native
        // has to be on the loader path before that. raw.SetProvider, NOT Batteries_V2.Init():
        // the bundle's dynamic loader probes Assembly.Location, which is empty under Costura,
        // and crashes at startup.
        static EventStore()
        {
            SqlCipherBootstrap.EnsureLoaded();
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlcipher());
        }

        public const string Extension = ".kcal";
        public const string DefaultFileName = "Default" + Extension;
        // 2: categories (events.categories assignment string + the categories definition table).
        // 3: repeats (repeat_* columns, skip_dates, and series_id / occurrence_start for the
        //    rows that replace a single date of a series).
        private const int SchemaVersion = 3;

        /// <summary>Killendars live in roaming APPDATA. Per-user always; a
        /// machine-wide install just means each user gets their own fresh Killendar.</summary>
        public static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Killendar");

        /// <summary>File name of the active Killendar inside DataDir, or an absolute path when the
        /// user opened one from elsewhere. Manage Killendars switches this.</summary>
        public static string ActiveFile =>
            Settings.Get("ActiveKillendar") is string s && !string.IsNullOrWhiteSpace(s)
                ? s : DefaultFileName;

        public static string ActivePath =>
            Path.IsPathRooted(ActiveFile) ? ActiveFile : Path.Combine(DataDir, ActiveFile);

        private SqliteConnection? _db;
        private string? _password;          // key of the open db (null = plaintext)
        private string _file = "";
        private List<CalendarEvent> _events = [];

        /// <summary>Raised after any add, update, delete or import.</summary>
        public event Action? Changed;

        public IReadOnlyList<CalendarEvent> Events => _events;

        /// <summary>Set when the last open or load failed; the UI surfaces it in the status bar.</summary>
        public string? LoadError { get; private set; }

        /// <summary>Set when the last open failed because the file needs a password. The unlock
        /// screen keys off this rather than parsing LoadError.</summary>
        public bool NeedsPassword { get; private set; }

        /// <summary>Count migrated out of events.json on this launch, for the status bar. 0 = none.</summary>
        public int MigratedCount { get; private set; }

        public bool IsOpen => _db != null;
        public bool HasPassword => _password != null;
        public string FilePath => _file;

        /// <summary>Name of the open Killendar without the extension, for the title bar.</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(_file) ? "" : Path.GetFileNameWithoutExtension(_file);

        /// <summary>Constructs an unopened store. Opening is driven by Security.cs, which has to
        /// decide between a silent open and an unlock prompt first.</summary>
        public EventStore() { }

        // ============================================================
        // Open / close
        // ============================================================

        /// <summary>Creates the data folder and migrates a pre-database events.json if that is all
        /// there is. Call once before the first Open, so the encryption probe has a file to look
        /// at. Never throws; a migration problem lands in LoadError.</summary>
        public void Prepare()
        {
            LoadError = null;
            NeedsPassword = false;
            try
            {
                Directory.CreateDirectory(DataDir);
                MigrateFromJsonIfNeeded();
            }
            catch (Exception ex) { LoadError = ex.Message; }
        }

        /// <summary>Prepare then open the active Killendar without prompting. Never throws: a key
        /// failure sets NeedsPassword, anything else sets LoadError, and the calendar comes up
        /// empty either way. Security.cs uses the prompting path instead; this exists for callers
        /// that just want a best-effort open.</summary>
        public void OpenActive(string? password = null)
        {
            Prepare();
            try { Open(ActivePath, password); }
            catch (SqliteException ex) when (IsKeyFailure(ex))
            {
                NeedsPassword = true;
                _events = [];
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                _events = [];
            }
        }

        /// <summary>Opens (creating if needed) a Killendar. Throws SqliteException on a wrong
        /// password. The key rides in the connection string, never as a PRAGMA after Open - see
        /// the remarks on Cs().</summary>
        public void Open(string path, string? password = null)
        {
            Close();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Built locally and only published to _db once it is genuinely usable. Assigning the
            // field first would make IsOpen true for a connection that failed to open or failed
            // its key check, and every caller keys off IsOpen to decide whether to prompt.
            var db = new SqliteConnection(Cs(path, password));
            try
            {
                db.Open();
                // Force the key check right here. Open() on its own validates nothing, and a lazy
                // failure surfaces later as a corrupt-looking calendar instead of a prompt.
                using var probe = db.CreateCommand();
                probe.CommandText = "SELECT count(*) FROM sqlite_master";
                probe.ExecuteScalar();
            }
            catch
            {
                db.Dispose();
                SqliteConnection.ClearAllPools();
                throw;
            }

            _db = db;
            _password = string.IsNullOrEmpty(password) ? null : password;
            _file = path;
            EnsureSchema();
            LoadIntoMemory();
            // Definitions are per-Killendar, so the cache is refreshed here rather than at the
            // call sites: SecurityController alone opens the store from six places, and a cache
            // left over from the previous file would paint this one's events in its colors.
            CategoryManager.Refresh(this);
        }

        public void Close()
        {
            _db?.Dispose();
            _db = null;
            _password = null;
            _file = "";
            // Dispose does NOT release the file handle - the pool keeps it. Anything that swaps
            // the file underneath (password change, switching Killendars) needs this.
            SqliteConnection.ClearAllPools();
        }

        /// <summary>
        /// Builds a connection string with the key embedded. Load-bearing, and NOT
        /// interchangeable with "PRAGMA key" after Open(): Microsoft.Data.Sqlite pools
        /// connections keyed on the whole connection string, so a pooled connection comes back
        /// already keyed and silently ignores a later PRAGMA - meaning A WRONG PASSWORD READS
        /// THE DATABASE. With the key in the connection string a wrong password gets its own
        /// pool entry and fails with SQLite error 26.
        /// </summary>
        private static string Cs(string path, string? password)
        {
            var b = new SqliteConnectionStringBuilder { DataSource = path };
            if (!string.IsNullOrEmpty(password)) b.Password = password;
            return b.ConnectionString;
        }

        /// <summary>SQLCipher reports a bad key as "file is not a database" (26), the same code a
        /// genuinely corrupt file gives. Treating both as "needs a password" is the safe way
        /// round: the unlock screen offers a New Killendar escape hatch either way, and we never
        /// silently start empty over a file that only needed a key.</summary>
        private static bool IsKeyFailure(SqliteException ex) =>
            ex.SqliteErrorCode == 26 || ex.SqliteExtendedErrorCode == 26;

        /// <summary>True when a .kcal exists and cannot be read without a key. Manage Killendars
        /// uses this for its encrypted flags.</summary>
        public static bool IsEncryptedFile(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using var probe = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
                probe.Open();
                using var cmd = probe.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM sqlite_master";
                cmd.ExecuteScalar();
                return false;
            }
            catch (SqliteException) { return true; }
            finally { SqliteConnection.ClearAllPools(); }
        }

        public static bool ActiveIsEncrypted() => IsEncryptedFile(ActivePath);

        // ============================================================
        // The Killendar files themselves. Manage Killendars drives all of this with the store
        // CLOSED, so every file - the active one included - is safe to rename, move or delete.
        // ============================================================

        /// <summary>Every .kcal in the data folder, name only, alphabetical.</summary>
        public static List<string> ListKillendars()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                return Directory.GetFiles(DataDir, "*" + Extension)
                                .Select(Path.GetFileName)
                                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                                .ToList()!;
            }
            catch { return []; }
        }

        /// <summary>Points the app at a different Killendar. Accepts a bare name inside DataDir or
        /// an absolute path anywhere.</summary>
        public static void SetActive(string nameOrPath) => Settings.Set("ActiveKillendar", nameOrPath);

        /// <summary>Creates an empty Killendar in the data folder and returns its file name. A
        /// zero-byte file is a valid empty SQLite database; the schema lands on first open, which
        /// keeps this cheap and means a create can never half-succeed.</summary>
        public static string CreateKillendar(string? baseName = null)
        {
            Directory.CreateDirectory(DataDir);
            string stem = string.IsNullOrWhiteSpace(baseName) ? "Killendar-new" : baseName!.Trim();
            string name = stem + Extension;
            for (int i = 2; File.Exists(Path.Combine(DataDir, name)); i++)
                name = stem + "-" + i + Extension;
            File.Create(Path.Combine(DataDir, name)).Dispose();
            return name;
        }

        /// <summary>Renames a Killendar inside the data folder. Retargets the active setting when
        /// the active file is the one renamed - without that the app would quietly create a fresh
        /// empty Killendar at the old name on the next open.</summary>
        public static void RenameKillendar(string oldName, string newName)
        {
            SqliteConnection.ClearAllPools();
            File.Move(Path.Combine(DataDir, oldName), Path.Combine(DataDir, newName));
            if (string.Equals(oldName, ActiveFile, StringComparison.OrdinalIgnoreCase))
                SetActive(newName);
        }

        public static void DeleteKillendar(string name)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(DataDir, name));
        }

        /// <summary>Copies a .kcal from anywhere into the data folder, uniquifying the name, and
        /// returns the name it landed under. Import rather than open-in-place: a Killendar is
        /// written to constantly, and silently writing into someone's Downloads folder or a network
        /// share is not what "Load" should mean.</summary>
        public static string ImportKillendar(string sourcePath)
        {
            Directory.CreateDirectory(DataDir);
            string stem = Path.GetFileNameWithoutExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(stem)) stem = "Imported";
            string name = stem + Extension;
            for (int i = 2; File.Exists(Path.Combine(DataDir, name)); i++)
                name = stem + "-" + i + Extension;
            File.Copy(sourcePath, Path.Combine(DataDir, name));
            return name;
        }

        // ============================================================
        // Password (SQLCipher). Two parts of this are load-bearing and easy to mistake for
        // ceremony: the forced finalization before the file swap, and ReplaceWithRetry.
        // ============================================================

        /// <summary>
        /// Sets, changes, or removes (null/empty) the password of the open Killendar by rewriting
        /// the file through sqlcipher_export with the new key, then reopening. The caller has
        /// already proven knowledge of the current password by having the file open.
        ///
        /// Not PRAGMA rekey: rekey cannot take a plaintext database to an encrypted one, which is
        /// the whole point of opt-in encryption. sqlcipher_export handles every direction.
        /// </summary>
        public void SetPassword(string? newPassword)
        {
            if (_db == null) throw new InvalidOperationException("no Killendar is open");
            if (string.IsNullOrEmpty(newPassword)) newPassword = null;

            string? oldPassword = _password;   // Close() clears it; kept for the rollback
            string path = _file;
            string tmp = path + ".rekey";
            if (File.Exists(tmp)) File.Delete(tmp);

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "ATTACH DATABASE $file AS rekeyed KEY $key";
                cmd.Parameters.AddWithValue("$file", tmp);
                cmd.Parameters.AddWithValue("$key", newPassword ?? "");
                cmd.ExecuteNonQuery();
            }
            Exec("SELECT sqlcipher_export('rekeyed')");
            Exec("DETACH DATABASE rekeyed");
            Close();

            // Clearing the pool alone is not always enough: a straggler
            // sqlite3 handle kept alive by a finalizer still has the old file mapped, and the swap
            // below then throws "being used by another process" however long we retry. Force
            // finalization so every native handle on both files is truly closed.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SqliteConnection.ClearAllPools();

            string bak = path + ".bak";
            try
            {
                ReplaceWithRetry(tmp, path, bak);
            }
            catch
            {
                // The swap failed even after retries - something else still holds the file (an AV
                // scan, a second handle). The original on disk is untouched, so bin the rewritten
                // copy, reopen with the OLD key (the app must never be left with no Killendar
                // open, or the next edit throws) and let the caller report it.
                try { File.Delete(tmp); } catch { /* best effort */ }
                Open(path, oldPassword);
                throw;
            }
            Open(path, newPassword);
            File.Delete(bak);   // only once the rewritten file opened cleanly
        }

        /// <summary>File.Replace with backoff retries. Antivirus and indexer scans of the freshly
        /// written rekey file cause transient sharing violations on the swap; waiting a moment and
        /// retrying beats failing the password change. If Replace never
        /// succeeds, falls back to a move-based swap - Replace needs simultaneous exclusive access
        /// to all three paths, while the moves need one file at a time and put the original back if
        /// the second move fails.</summary>
        private static void ReplaceWithRetry(string source, string dest, string backup)
        {
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try { File.Replace(source, dest, backup); return; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(150 * attempt);   // ~150ms .. 1.2s
                }
            }
            if (File.Exists(backup)) File.Delete(backup);   // stale .bak from a past failure
            File.Move(dest, backup);
            try { File.Move(source, dest); }
            catch
            {
                File.Move(backup, dest);   // put the original back; caller reports the error
                throw;
            }
        }

        /// <summary>Closes the active Killendar and renames it aside, for the unlock screen's
        /// "New Killendar" escape hatch. Returns the archived path. The locked file is kept, never
        /// deleted - a forgotten password is not recoverable, but the data is not ours to
        /// destroy.</summary>
        public static string ArchiveActive()
        {
            string src = ActivePath;
            string dest = Path.Combine(
                Path.GetDirectoryName(src) ?? DataDir,
                Path.GetFileNameWithoutExtension(src) +
                    "-locked-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + Extension);
            SqliteConnection.ClearAllPools();
            File.Move(src, dest);
            return dest;
        }

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

        /// <summary>Writes, then repaints. A write failure (disk full, profile locked) is recorded
        /// in LoadError and the session keeps working in memory rather than throwing at the user
        /// mid-edit - the same bargain the JSON store made.</summary>
        private void Commit(Action write)
        {
            try { if (_db != null) write(); LoadError = null; }
            catch (Exception ex) { LoadError = ex.Message; }
            Changed?.Invoke();
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

            var duration = edited.End > edited.Start ? edited.End - edited.Start : TimeSpan.FromHours(1);

            // How far the user moved the occurrence they were looking at.
            var origin = edited.OccurrenceStart ?? master.Start;
            var delta  = edited.Start - origin;

            master.Title       = edited.Title;
            master.Location    = edited.Location;
            master.Description = edited.Description;
            master.Attendees   = [.. edited.Attendees ?? []];
            master.Categories  = edited.Categories;
            master.AllDay      = edited.AllDay;

            master.Start += delta;
            master.End   = master.Start + duration;

            master.Repeat         = edited.Repeat;
            master.RepeatInterval = edited.RepeatInterval > 0 ? edited.RepeatInterval : 1;
            master.RepeatDays     = [.. edited.RepeatDays ?? []];
            master.RepeatUntil    = edited.RepeatUntil;
            master.RepeatCount    = edited.RepeatCount;

            // Deleted dates move with the series. Leaving them put would silently un-delete the
            // date the user removed and delete a different one instead.
            if (delta.Days != 0)
            {
                var shifted = new List<DateTime>();
                foreach (var d in master.SkipDates) shifted.Add(d.Date.AddDays(delta.Days));
                master.SkipDates = shifted;
            }

            Update(master);
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
            if (stray != null) Delete(stray.Id);

            var master = GetSeriesMaster(occ);
            if (master == null) return;
            if (master.SkipDates.Any(d => d.Date == day)) return;

            master.SkipDates.Add(day);
            Update(master);
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

        // ============================================================
        // Queries. Served from memory - identical semantics to the JSON store.
        // ============================================================

        public CalendarEvent? GetById(Guid id) => _events.FirstOrDefault(e => e.Id == id);

        /// <summary>The series master an occurrence belongs to, or null.</summary>
        public CalendarEvent? GetSeriesMaster(CalendarEvent ev)
        {
            if (ev == null) return null;
            var key = ev.SeriesKey;
            return _events.FirstOrDefault(e => e.Id == key && e.IsSeries);
        }

        /// <summary>
        /// All appointments overlapping the half-open interval [start, end), earliest first.
        ///
        /// This is where repeats become visible. A series is ONE stored row; its dates are
        /// generated here, so every view and the day agenda got repeats the moment this method
        /// did, without any of them changing. Three kinds of row are handled:
        ///   - plain appointments, included when they overlap, exactly as before;
        ///   - series masters, expanded into occurrences by Recurrence;
        ///   - overrides, which stand in for one generated date and are included on their own
        ///     dates - so an occurrence moved to another day appears there, not here.
        /// </summary>
        public List<CalendarEvent> GetInRange(DateTime start, DateTime end)
        {
            var result = new List<CalendarEvent>();

            // Which generated dates have been replaced by an edited row. Keyed by date, because
            // the override's own Start is wherever the user moved it to, and the thing being
            // replaced is the date the series WOULD have produced.
            var replaced = new HashSet<(Guid Series, DateTime Day)>();
            foreach (var e in _events)
                if (e.IsOverride && e.OccurrenceStart.HasValue)
                    replaced.Add((e.SeriesId!.Value, e.OccurrenceStart.Value.Date));

            foreach (var e in _events)
            {
                if (e.IsSeries)
                {
                    foreach (var occ in Recurrence.Expand(e, start, end))
                        if (!replaced.Contains((e.Id, occ.OccurrenceStart!.Value.Date)))
                            result.Add(occ);
                    continue;
                }

                // Plain appointments AND overrides: both are real rows shown on their own dates.
                if (e.Start < end && e.End > start) result.Add(e);
            }

            return [.. result.OrderBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Start)];
        }

        /// <summary>All events touching a calendar date, all-day entries first.</summary>
        public List<CalendarEvent> GetOnDay(DateTime date)
            => GetInRange(date.Date, date.Date.AddDays(1));

        /// <summary>
        /// The next <paramref name="count"/> appointments starting at or after
        /// <paramref name="from"/>. Repeats are included, which is why this walks forward a window
        /// at a time instead of reading the row list: a series has no rows to read.
        /// </summary>
        public List<CalendarEvent> GetUpcoming(DateTime from, int count)
        {
            if (count <= 0) return [];

            // Widening windows rather than one huge range, so the common case (something on in the
            // next fortnight) does not expand five years of every series to find it.
            foreach (int days in new[] { 14, 90, 400, 1500 })
            {
                var found = GetInRange(from, from.AddDays(days))
                            .Where(e => e.End > from)
                            .OrderBy(e => e.Start)
                            .Take(count)
                            .ToList();
                if (found.Count >= count) return found;

                // Last window: return what there is rather than looping forever on a calendar
                // that genuinely has nothing further out.
                if (days == 1500) return found;
            }
            return [];
        }

        // ============================================================
        // Migration from the pre-1.0 events.json
        // ============================================================

        /// <summary>
        /// Moves a pre-database events.json into Default.kcal, once. Runs only when the target
        /// .kcal does not exist yet, so it can never overwrite a real Killendar.
        ///
        /// Both LOCALAPPDATA and APPDATA are checked: the JSON store wrote to LOCALAPPDATA while
        /// Killendars live in roaming APPDATA, and a build in between could have used either.
        ///
        /// The old file is RENAMED to .migrated, not deleted. Converting the JSON's naive local
        /// DateTimes to UTC is the one lossy step in this whole feature, and if the zone is wrong
        /// for someone the original has to still be there.
        /// </summary>
        private void MigrateFromJsonIfNeeded()
        {
            MigratedCount = 0;
            string target = ActivePath;
            if (File.Exists(target)) return;

            string? json = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "Killendar", "events.json"),
                Path.Combine(DataDir, "events.json"),
            }.FirstOrDefault(File.Exists);
            if (json == null) return;

            List<CalendarEvent> old;
            try
            {
                old = JsonSerializer.Deserialize<List<CalendarEvent>>(File.ReadAllText(json),
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? [];
            }
            catch (Exception ex)
            {
                // A corrupt events.json must not block the app from starting on a fresh
                // Killendar, and must not be renamed away either - it is the only copy.
                LoadError = ex.Message;
                return;
            }

            // Written plaintext deliberately: migration cannot invent a password, and the lock
            // button is how the user opts in afterwards.
            using (var c = new SqliteConnection(Cs(target, null)))
            {
                c.Open();
                using var schema = c.CreateCommand();
                schema.CommandText = SchemaSql;
                schema.ExecuteNonQuery();
                // Seeded here, not on the next open: EnsureSchema only seeds when it finds no
                // categories table, and the line above has just created one.
                SeedDefaultCategories(c);

                using var tx = c.BeginTransaction();
                foreach (var ev in old)
                {
                    // The JSON holds naive local DateTimes. Stamp them Local so ToStore's
                    // ToUniversalTime() uses the machine zone rather than treating them as
                    // already-UTC, which would shift every appointment by the offset.
                    if (!ev.AllDay)
                    {
                        ev.Start = DateTime.SpecifyKind(ev.Start, DateTimeKind.Local);
                        ev.End   = DateTime.SpecifyKind(ev.End,   DateTimeKind.Local);
                    }
                    Upsert(c, ev);
                }
                tx.Commit();
            }
            SqliteConnection.ClearAllPools();

            try
            {
                string keep = json + ".migrated";
                if (File.Exists(keep)) File.Delete(keep);
                File.Move(json, keep);
            }
            catch
            {
                // The .kcal is written and that is what matters. The next launch sees the .kcal
                // already exists and returns early, so a stuck rename cannot cause a re-migration.
            }

            MigratedCount = old.Count;
        }
    }
}
