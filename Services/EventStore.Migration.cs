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
            if (File.Exists(target)) return;

            string? json = new[]
            {
                Path.Combine(AppPaths.LocalData, "Killendar", "events.json"),
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
                // Killendar, and must not be renamed away either, because it is the only copy.
                // The warning is kept apart from LoadError so the calendar still opens.
                MigrationWarning = json + ": " + ex.Message;
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
                // Move never overwrites a calendar created by another process during migration.
                File.Move(temporary, target);
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
    }
}
