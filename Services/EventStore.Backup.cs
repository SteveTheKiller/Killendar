using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        /// <summary>Takes a verified backup and prunes the old ones. Returns its path, or null with
        /// LastBackupError set. Never throws: a backup is a safety net, not a reason to stop.</summary>
        public string? BackupNow(DateTime now)
        {
            if (_db == null || LoadError != null) return null;
            lock (_writeGate)
            {
                string final = Path.Combine(BackupDir,
                    Path.GetFileNameWithoutExtension(_file) + " " + now.ToString(BackupStamp, CultureInfo.InvariantCulture) + Extension);
                try
                {
                    Directory.CreateDirectory(BackupDir);
                    string staged = final + ".partial";
                    ExportVerified(_db, staged, _password);
                    try
                    {
                        if (File.Exists(final)) File.Delete(final);   // same second: the newer copy wins
                        File.Move(staged, final);
                    }
                    catch
                    {
                        TryDelete(staged);
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    LastBackupError = ex.Message;
                    return null;
                }
                LastBackupError = null;
                Prune();
                return final;
            }
        }

        /// <summary>After a password change, rewrites every backup under the new key, so a
        /// calendar that was just encrypted leaves no readable copy behind and an old password
        /// opens nothing. A backup that cannot be rewritten is left as it was and reported.</summary>
        private void RekeyBackups(string? oldPassword, string? newPassword)
        {
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
                }
            }
        }

        /// <summary>Exports source into a new file under key, then reopens that file and checks it
        /// holds every appointment the source does. Throws, leaving no file, when it does not.</summary>
        private static void ExportVerified(SqliteConnection source, string destination, string? key)
        {
            TryDelete(destination);
            long expected;
            using (var count = source.CreateCommand())
            {
                count.CommandText = "SELECT count(*) FROM main.events";
                expected = (long)count.ExecuteScalar()!;
            }
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
                if ((long)verify.ExecuteScalar()! != expected)
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
        private void Prune()
        {
            var all = ListBackups();
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
