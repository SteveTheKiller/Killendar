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
    public partial class EventStore
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

        /// <summary>Default per-user folder. Manage Killendars can override it, including with
        /// the executable folder for a self-contained portable copy.</summary>
        public static string DefaultDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Killendar");

        public static string DataDir => Settings.Get("KillendarDataDir") is string configured &&
                                        !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : DefaultDataDir;

        public static void SetDataDir(string path)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(path);
            // Fail before persisting an unusable location (notably Program Files when an installed
            // copy offers portable mode without elevation).
            string probe = Path.Combine(path, ".killendar-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (File.Create(probe)) { }
            File.Delete(probe);
            Settings.Set("KillendarDataDir", path);
            SetActive(DefaultFileName);
        }

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
    }
}
