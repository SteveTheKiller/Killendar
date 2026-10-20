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
            => SetPassword(newPassword, File.Replace);

        /// <summary>Changes the password using the supplied filesystem replacement operation.</summary>
        public void SetPassword(string? newPassword, Action<string, string, string> replaceFile)
        {
            if (replaceFile == null) throw new ArgumentNullException(nameof(replaceFile));
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
                ReplaceWithRetry(tmp, path, bak, replaceFile);
            }
            catch (Exception swapError)
            {
                // Replacement can fail after moving the original to the backup. Keep both
                // complete copies until the old calendar has actually reopened.
                try
                {
                    RestoreReplacementDestination(path, bak);
                    using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    Open(path, oldPassword);
                }
                catch (Exception recoveryError)
                {
                    throw new IOException("The calendar could not reopen after the password change failed. Recovery files were kept.",
                        new AggregateException(swapError, recoveryError));
                }
                try { File.Delete(tmp); } catch { /* best effort */ }
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
        private static void ReplaceWithRetry(string source, string dest, string backup, Action<string, string, string> replaceFile)
        {
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try { replaceFile(source, dest, backup); return; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    RestoreReplacementDestination(dest, backup);
                    System.Threading.Thread.Sleep(150 * attempt);   // ~150ms .. 1.2s
                }
            }
            RestoreReplacementDestination(dest, backup);
            if (File.Exists(backup)) File.Delete(backup);   // the original is present at dest
            File.Move(dest, backup);
            try { File.Move(source, dest); }
            catch
            {
                File.Move(backup, dest);   // put the original back; caller reports the error
                throw;
            }
        }

        private static void RestoreReplacementDestination(string destination, string backup)
        {
            if (File.Exists(destination)) return;
            if (!File.Exists(backup)) throw new IOException("The original calendar is unavailable. Recovery files were kept.");
            string restored = destination + ".restore-" + Guid.NewGuid().ToString("N");
            File.Copy(backup, restored);
            File.Move(restored, destination);
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
    }
}
