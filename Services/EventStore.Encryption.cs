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
    }
}
