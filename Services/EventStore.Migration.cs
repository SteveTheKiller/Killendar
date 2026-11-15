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
            MigrationWarning = null;
            string target = ActivePath;
            // Written when a legacy file could not be imported, so a later launch knows the empty
            // calendar next to it was made as a stand-in and the file may be tried again.
            string marker = target + ".legacy-retry";
            bool targetExists = File.Exists(target);
            if (targetExists && !File.Exists(marker)) return;

            string? json = new[]
            {
                Path.Combine(AppPaths.LocalData, "Killendar", "events.json"),
                Path.Combine(DataDir, "events.json"),
            }.FirstOrDefault(File.Exists);
            if (json == null)
            {
                if (targetExists) TryDelete(marker);
                return;
            }

            // The stand-in is replaced only while it is still the untouched empty calendar. Once
            // the user has added anything, or a calendar is open, it is theirs and stays.
            bool replaceStandIn = false;
            if (targetExists)
            {
                if (_db != null) return;
                if (!IsUntouchedStandIn(target)) { TryDelete(marker); return; }
                replaceStandIn = true;
            }

            List<CalendarEvent> old;
            try
            {
                old = (JsonSerializer.Deserialize<List<CalendarEvent>>(File.ReadAllText(json),
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? []).Where(ev => ev != null).ToList();
            }
            catch (Exception ex)
            {
                // A corrupt events.json must not block the app from starting on a fresh
                // Killendar, and must not be renamed away either, because it is the only copy.
                // The warning is kept apart from LoadError so the calendar still opens.
                MigrationWarning = json + ": " + ex.Message;
                WriteRetryMarker(marker, json);
                return;
            }

            // Old builds could write the same id twice. Every copy is a real appointment, so a
            // repeated or empty id gets a new one instead of failing the count check below.
            var ids = new HashSet<Guid>();
            foreach (var ev in old)
                if (ev.Id == Guid.Empty || !ids.Add(ev.Id))
                {
                    ev.Id = Guid.NewGuid();
                    ids.Add(ev.Id);
                }

            // Written plaintext deliberately: migration cannot invent a password, and the lock
            // button is how the user opts in afterwards.
            string temporary = target + ".migrate-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var c = new SqliteConnection(Cs(temporary, null)))
                {
                    c.Open();
                    using var schema = c.CreateCommand();
                    schema.CommandText = SchemaSql;
                    schema.ExecuteNonQuery();
                    SeedDefaultCategories(c);

                    using var tx = c.BeginTransaction();
                    foreach (var ev in old)
                    {
                        // Legacy timed values are local; all-day values remain floating dates.
                        if (!ev.AllDay)
                        {
                            ev.Start = DateTime.SpecifyKind(ev.Start, DateTimeKind.Local);
                            ev.End = DateTime.SpecifyKind(ev.End, DateTimeKind.Local);
                        }
                        Upsert(c, ev);
                    }
                    var validation = new EventStore { _db = c };
                    validation.LoadIntoMemory();
                    validation.ListCategories();
                    if (validation.Events.Count != old.Count)
                        throw new InvalidDataException("The migrated calendar did not retain every appointment.");
                    tx.Commit();
                }
                SqliteConnection.ClearAllPools();
                if (replaceStandIn)
                    foreach (string stale in new[] { target, target + "-wal", target + "-shm", target + "-journal" })
                        File.Delete(stale);
                // Move never overwrites a calendar created by another process during migration.
                File.Move(temporary, target);
                TryDelete(marker);
            }
            catch
            {
                // Nothing was published and events.json is untouched; remember that it is still owed.
                WriteRetryMarker(marker, json);
                throw;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            try
            {
                string keep = json + ".migrated";
                if (File.Exists(keep)) keep += "." + Guid.NewGuid().ToString("N");
                File.Move(json, keep);
            }
            catch
            {
                // The .kcal is written and that is what matters. The next launch sees the .kcal
                // already exists and returns early, so a stuck rename cannot cause a re-migration.
            }

            MigratedCount = old.Count;
        }

        private static void WriteRetryMarker(string marker, string json)
        {
            try { File.WriteAllText(marker, json); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>True for a plaintext calendar that holds no appointments and only the
        /// default categories, which is all the stand-in made after a failed import ever has.</summary>
        private static bool IsUntouchedStandIn(string path)
        {
            try
            {
                using var c = new SqliteConnection(Cs(path, null));
                c.Open();
                var probe = new EventStore { _db = c };
                probe.LoadIntoMemory();
                var categories = probe.ListCategories();
                return probe.Events.Count == 0
                    && categories.Count == DefaultCategories.Length
                    && DefaultCategories.All(d => categories.Contains(d));
            }
            catch (Exception) { return false; }
            finally { SqliteConnection.ClearAllPools(); }
        }
    }
}
