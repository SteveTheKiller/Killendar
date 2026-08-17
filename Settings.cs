using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Killendar
{
    /// <summary>
    /// Flat key/value settings in %LOCALAPPDATA%\Killendar\settings.json. Deliberately dumb: the
    /// kit only ever stores short strings (theme name, accent name, locale, window rect), and a
    /// JSON file keeps the app xcopy-portable, with no registry footprint to clean up.
    /// Every read and write is best-effort - a corrupt or unwritable file costs the saved theme,
    /// never a crash on startup.
    /// </summary>
    internal static class Settings
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Killendar");
        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        private static Dictionary<string, string>? _cache;

        private static Dictionary<string, string> Load()
        {
            if (_cache != null) return _cache;
            try
            {
                _cache = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))
                      ?? []
                    : [];
            }
            catch { _cache = []; }
            return _cache;
        }

        internal static string? Get(string key)
        {
            var d = Load();
            return d.TryGetValue(key, out var v) ? v : null;
        }

        internal static void Set(string key, string value)
        {
            var d = Load();
            d[key] = value;
            try
            {
                Directory.CreateDirectory(Dir);
                // Write to a temp file and swap, so a crash mid-write cannot leave a truncated
                // settings file that loses every stored key at once.
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { /* unwritable profile - the setting just does not persist */ }
        }
    }
}
