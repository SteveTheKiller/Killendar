using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>A password change that returned has committed: the calendar is open with the new
    /// password. A non-null RetainedBackup is the previous file, left behind because it could not
    /// be deleted, and the caller must tell the user where it is.</summary>
    public sealed class PasswordChangeOutcome(string? retainedBackup, bool retainedBackupIsUnencrypted)
    {
        public string? RetainedBackup { get; } = retainedBackup;
        public bool RetainedBackupIsUnencrypted { get; } = retainedBackupIsUnencrypted;
    }

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
        public PasswordChangeOutcome SetPassword(string? newPassword)
            => SetPassword(newPassword, File.Replace);

        /// <summary>Changes the password using the supplied filesystem replacement operation.</summary>
        public PasswordChangeOutcome SetPassword(string? newPassword, Action<string, string, string> replaceFile)
            => SetPassword(newPassword, replaceFile, File.Delete);

        /// <summary>Changes the password using supplied replacement and owned restoration-copy deletion operations.</summary>
        public PasswordChangeOutcome SetPassword(string? newPassword, Action<string, string, string> replaceFile, Action<string> deleteRestorationCopy)
        {
            if (replaceFile == null) throw new ArgumentNullException(nameof(replaceFile));
            if (deleteRestorationCopy == null) throw new ArgumentNullException(nameof(deleteRestorationCopy));
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
            try { Exec("SELECT sqlcipher_export('rekeyed')"); }
            catch (Exception exportError)
            {
                try { Exec("DETACH DATABASE rekeyed"); }
                catch (Exception detachError)
                {
                    Close();
                    throw new IOException("The password export failed and its temporary database could not detach. " +
                        "The calendar was closed. Original: " + path + ". Temporary (" + Protection(newPassword) + "): " + tmp,
                        new AggregateException(exportError, detachError));
                }
                // The stage may hold part of the calendar, unencrypted when the password was being
                // removed. It is ours, so it goes; if it cannot, the user has to know where it is.
                try { if (File.Exists(tmp)) deleteRestorationCopy(tmp); }
                catch (Exception cleanupError)
                {
                    throw new IOException("The password change failed and its " + Protection(newPassword) +
                        " temporary copy could not be removed. Retained: " + tmp,
                        new AggregateException(exportError, cleanupError));
                }
                throw;
            }
            try { Exec("DETACH DATABASE rekeyed"); }
            catch
            {
                Close();
                throw;
            }
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
                ReplaceWithRetry(tmp, path, bak, replaceFile, deleteRestorationCopy);
            }
            catch (Exception swapError)
            {
                // Replacement can fail after moving the original to the backup. Keep both
                // complete copies until the old calendar has actually reopened.
                try
                {
                    RestoreReplacementDestination(path, bak, deleteRestorationCopy);
                    using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    Open(path, oldPassword);
                }
                catch (Exception recoveryError)
                {
                    throw new IOException("The calendar could not reopen after the password change failed. Recovery files were kept. Backup: " + bak
                        + ". Staged: " + tmp + ". Swap: " + swapError.Message + " Recovery: " + recoveryError.Message,
                        new AggregateException(swapError, recoveryError));
                }
                try { File.Delete(tmp); } catch { /* best effort */ }
                throw;
            }
            Open(path, newPassword);

            // The change has committed once the rewritten file opens. A backup that will not delete
            // (antivirus, permissions) is reported, not thrown: throwing here would tell the user a
            // committed password change had failed.
            try { deleteRestorationCopy(bak); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new PasswordChangeOutcome(bak, oldPassword == null);
            }
            return new PasswordChangeOutcome(null, false);
        }

        private static string Protection(string? password) => password == null ? "unencrypted" : "encrypted";

        /// <summary>File.Replace with backoff retries. Antivirus and indexer scans of the freshly
        /// written rekey file cause transient sharing violations on the swap; waiting a moment and
        /// retrying beats failing the password change. If Replace never
        /// succeeds, falls back to a move-based swap - Replace needs simultaneous exclusive access
        /// to all three paths, while the moves need one file at a time and put the original back if
        /// the second move fails.</summary>
        private static void ReplaceWithRetry(string source, string dest, string backup, Action<string, string, string> replaceFile, Action<string> deleteRestorationCopy)
        {
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try { replaceFile(source, dest, backup); return; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    RestoreReplacementDestination(dest, backup, deleteRestorationCopy);
                    System.Threading.Thread.Sleep(150 * attempt);   // ~150ms .. 1.2s
                }
            }
            RestoreReplacementDestination(dest, backup, deleteRestorationCopy);
            if (File.Exists(backup)) File.Delete(backup);   // the original is present at dest
            File.Move(dest, backup);
            try { File.Move(source, dest); }
            catch
            {
                File.Move(backup, dest);   // put the original back; caller reports the error
                throw;
            }
        }

        private static void RestoreReplacementDestination(string destination, string backup, Action<string> deleteRestorationCopy)
        {
            if (File.Exists(destination)) return;
            if (!File.Exists(backup)) throw new IOException("The original calendar is unavailable. Recovery files were kept.");
            string restored = destination + ".restore-" + Guid.NewGuid().ToString("N");
            using var input = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read);
            bool ownsRestored = false;
            try
            {
                using (var output = new FileStream(restored, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsRestored = true;
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                File.Move(restored, destination);
                ownsRestored = false;
            }
            catch (Exception recoveryError)
            {
                if (ownsRestored)
                {
                    try { deleteRestorationCopy(restored); }
                    catch (Exception cleanupError)
                    {
                        throw new IOException("The temporary calendar restoration copy could not be removed. Retained: " + restored,
                            new AggregateException(recoveryError, cleanupError));
                    }
                }
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
