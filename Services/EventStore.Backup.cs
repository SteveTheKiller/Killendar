using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Killendar.Services
{
    /// <summary>One verified snapshot in the Backups folder beside its calendar.</summary>
    public sealed record BackupInfo(string Path, DateTime Taken);

    public partial class EventStore
    {
        // ============================================================
        // Backups. A snapshot is exported through sqlcipher_export under the calendar's own key,
        // reopened and counted before it is given its final name, and only then are older ones
        // pruned. A backup that fails never costs an earlier one.
        // ============================================================

        private const string BackupStamp = "yyyy-MM-dd HHmmss";
        private const int DailyKept = 7, WeeklyKept = 4;

        /// <summary>The daily backup still reading the file, if any. Close waits for it, so a
        /// rename, password change or switch never finds the file held open.</summary>
        private Task<string?>? _backgroundBackup;

        /// <summary>Waits for a running daily backup to let go of the file.</summary>
        private void WaitForBackgroundBackup()
        {
            var running = _backgroundBackup;
            _backgroundBackup = null;
            try { running?.Wait(TimeSpan.FromSeconds(60)); } catch (AggregateException) { /* reported through LastBackupError */ }
        }

        /// <summary>Why the last backup failed; null after one that verified.</summary>
        public string? LastBackupError { get; private set; }

        private string BackupDir => BackupDirFor(_file);

        private static string BackupDirFor(string calendarPath) =>
            Path.Combine(Path.GetDirectoryName(calendarPath) ?? DataDir, "Backups");

        /// <summary>This calendar's backups, newest first.</summary>
        public List<BackupInfo> ListBackups() => ListBackupsFor(_file);

        /// <summary>The backups of the calendar at calendarPath, newest first. Manage Killendars
        /// uses this while the store is closed.</summary>
        public static List<BackupInfo> ListBackupsFor(string calendarPath)
        {
            if (string.IsNullOrEmpty(calendarPath)) return [];
            string dir = BackupDirFor(calendarPath);
            if (!Directory.Exists(dir)) return [];
            string prefix = Path.GetFileNameWithoutExtension(calendarPath) + " ";
            var found = new List<BackupInfo>();
            foreach (var path in Directory.GetFiles(dir, "*" + Extension))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (DateTime.TryParseExact(stem.Substring(prefix.Length), BackupStamp,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken))
                    found.Add(new BackupInfo(path, taken));
            }
            return found.OrderByDescending(b => b.Taken).ToList();
        }

        /// <summary>The first open of each day takes a backup; later opens that day do not.</summary>
        public string? BackupIfDue(DateTime now) =>
            ListBackups().Any(b => b.Taken.Date == now.Date) ? null : BackupNow(now);

        /// <summary>
        /// The daily backup, on a worker thread so a large calendar opens without waiting for it.
        /// It reads the file through a connection of its own - the app's connection is never shared
        /// across threads - and SQLite's file locking keeps it consistent beside the app's writes.
        /// Completes with the backup's path, or null when none was due or it failed (then
        /// LastBackupError says why).
        /// </summary>
        public Task<string?> BackupIfDueAsync(DateTime now)
        {
            if (_db == null || ListBackups().Any(b => b.Taken.Date == now.Date)) return Task.FromResult<string?>(null);
            if (LoadError != null)
            {
                LastBackupError = LoadError;
                return Task.FromResult<string?>(null);
            }
            string file = _file;
            string? password = _password;
            return _backgroundBackup = Task.Run(() =>
            {
                try
                {
                    var cs = new SqliteConnectionStringBuilder
                        { DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
                    // ReadWriteCreate: ATTACH creates the backup file under the same flags.
                    if (!string.IsNullOrEmpty(password)) cs.Password = password;
                    using var source = new SqliteConnection(cs.ToString());
                    source.Open();
                    string written = WriteBackup(source, file, password, now);
                    LastBackupError = null;
                    return (string?)written;
                }
                catch (Exception ex)
                {
                    LastBackupError = ex.Message;
                    return null;
                }
            });
        }

        /// <summary>Takes a verified backup and prunes the old ones. Returns its path, or null with
        /// LastBackupError set. Never throws: a backup is a safety net, not a reason to stop.</summary>
        public string? BackupNow(DateTime now)
        {
            if (_db == null) return null;
            if (LoadError != null)
            {
                LastBackupError = LoadError;   // a calendar that failed to load is not backed up over a good copy
                return null;
            }
            lock (_writeGate)
            {
                try
                {
                    string written = WriteBackup(_db, _file, _password, now);
                    LastBackupError = null;
                    return written;
                }
                catch (Exception ex)
                {
                    LastBackupError = ex.Message;
                    return null;
                }
            }
        }

        /// <summary>Exports, verifies and names one backup of <paramref name="file"/> read through
        /// <paramref name="source"/>, then prunes. Throws, leaving earlier backups as they were.</summary>
        private static string WriteBackup(SqliteConnection source, string file, string? password, DateTime now)
        {
            string dir = BackupDirFor(file);
            string final = Path.Combine(dir,
                Path.GetFileNameWithoutExtension(file) + " " + now.ToString(BackupStamp, CultureInfo.InvariantCulture) + Extension);
            Directory.CreateDirectory(dir);
            string staged = final + ".partial";
            ExportVerified(source, staged, password);
            // Same second: the newer copy wins, but the earlier one is only set aside until
            // the newer is in place, so a failed move costs nothing.
            string aside = final + ".old";
            try
            {
                TryDelete(aside);
                if (File.Exists(final)) File.Move(final, aside);
                File.Move(staged, final);
                TryDelete(aside);
            }
            catch
            {
                TryDelete(staged);
                try { if (!File.Exists(final) && File.Exists(aside)) File.Move(aside, final); } catch { /* best effort */ }
                throw;
            }
            Prune(file);
            return final;
        }

        /// <summary>Backups the last password change could not rewrite, which still open with the
        /// old password (or none). Empty when every one was rewritten.</summary>
        public List<string> BackupsNotRekeyed { get; } = [];

        /// <summary>After a password change, rewrites every backup under the new key, so a
        /// calendar that was just encrypted leaves no readable copy behind and an old password
        /// opens nothing. A backup that cannot be rewritten is left as it was and reported.</summary>

        private void RekeyBackups(string? oldPassword, string? newPassword)
        {
            BackupsNotRekeyed.Clear();
            foreach (var backup in ListBackups())
            {
                string staged = backup.Path + ".rekey";
                try
                {
                    using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource = backup.Path, Password = oldPassword ?? "", Pooling = false }.ToString()))
                    {
                        source.Open();
                        ExportVerified(source, staged, newPassword);
                    }
                    File.Delete(backup.Path);
                    File.Move(staged, backup.Path);
                }
                catch (Exception ex)
                {
                    TryDelete(staged);
                    LastBackupError = ex.Message;
                    BackupsNotRekeyed.Add(backup.Path);
                }
            }
        }

        /// <summary>Exports source into a new file under key, then reopens that file and checks it
        /// holds every appointment the source does. Throws, leaving no file, when it does not.</summary>
        private static void ExportVerified(SqliteConnection source, string destination, string? key)
        {
            TryDelete(destination);
            long CountSource()
            {
                using var count = source.CreateCommand();
                count.CommandText = "SELECT count(*) FROM main.events";
                return (long)count.ExecuteScalar()!;
            }
            // Counted before and after: the daily backup reads beside the app, which may save an
            // appointment while the export runs, and the copy then matches one side or the other.
            long before = CountSource(), after = before;
            try
            {
                using (var attach = source.CreateCommand())
                {
                    attach.CommandText = "ATTACH DATABASE $file AS backup KEY $key";
                    attach.Parameters.AddWithValue("$file", destination);
                    attach.Parameters.AddWithValue("$key", key ?? "");
                    attach.ExecuteNonQuery();
                }
                try
                {
                    using var export = source.CreateCommand();
                    export.CommandText = "SELECT sqlcipher_export('backup')";
                    export.ExecuteNonQuery();
                }
                finally
                {
                    using var detach = source.CreateCommand();
                    detach.CommandText = "DETACH DATABASE backup";
                    detach.ExecuteNonQuery();
                }
                after = CountSource();

                var check = new SqliteConnectionStringBuilder
                    { DataSource = destination, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
                if (!string.IsNullOrEmpty(key)) check.Password = key;
                using var copy = new SqliteConnection(check.ToString());
                copy.Open();
                using var verify = copy.CreateCommand();
                verify.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(verify.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                    throw new IOException("The backup failed its integrity check.");
                verify.CommandText = "SELECT count(*) FROM events";
                long copied = (long)verify.ExecuteScalar()!;
                if (copied != before && copied != after)
                    throw new IOException("The backup is missing appointments.");
            }
            catch
            {
                TryDelete(destination);
                throw;
            }
        }

        /// <summary>Keeps every backup of the latest day, the newest of each of the six days before it
        /// that have one, and the newest of each of the four weeks before those.</summary>
        private static void Prune(string file)
        {
            var all = ListBackupsFor(file);
            var days = all.GroupBy(b => b.Taken.Date).Take(DailyKept).ToList();
            var keep = new HashSet<string>(days.Select(d => d.First().Path), StringComparer.OrdinalIgnoreCase);
            if (days.Count > 0) keep.UnionWith(days[0].Select(b => b.Path));   // today's stay until tomorrow
            var dailyDates = new HashSet<DateTime>(days.Select(d => d.Key));
            foreach (var week in all.Where(b => !dailyDates.Contains(b.Taken.Date))
                                    .GroupBy(b => WeekOf(b.Taken)).Take(WeeklyKept))
                keep.Add(week.First().Path);
            foreach (var old in all.Where(b => !keep.Contains(b.Path)))
                TryDelete(old.Path);
        }

        /// <summary>A deleted calendar's backups go with it: the delete promises the appointments
        /// are gone, and a new calendar of the same name must not inherit them.</summary>
        private static void DeleteBackups(string calendarPath)
        {
            foreach (var backup in ListBackupsFor(calendarPath)) TryDelete(backup.Path);
        }

        /// <summary>Renames a calendar's backups along with it, so they stay listed under it.</summary>
        private static void RenameBackups(string oldPath, string newPath)
        {
            string stem = Path.GetFileNameWithoutExtension(newPath) + " ";
            foreach (var backup in ListBackupsFor(oldPath))
            {
                string target = Path.Combine(Path.GetDirectoryName(backup.Path)!,
                    stem + backup.Taken.ToString(BackupStamp, CultureInfo.InvariantCulture) + Extension);
                try { if (!File.Exists(target)) File.Move(backup.Path, target); } catch { /* best effort */ }
            }
        }

        private static DateTime WeekOf(DateTime taken) =>
            taken.Date.AddDays(-(((int)taken.DayOfWeek + 6) % 7));   // the Monday that starts it

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }
}
