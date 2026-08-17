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
    }
}
