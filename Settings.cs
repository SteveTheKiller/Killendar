using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private static readonly string Dir = Path.Combine(AppPaths.LocalData, "Killendar");
        internal static readonly string FilePath = Path.Combine(Dir, "settings.json");

        private static Dictionary<string, string>? _cache;
        private static readonly object Gate = new object();

        private static Dictionary<string, string> Load()
        {
            if (_cache != null) return _cache;
            foreach (string path in new[] { FilePath, FilePath + ".bak", FilePath + ".tmp" })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                    if (values != null) return _cache = values;
                }
                catch { /* Try the next complete recovery copy. */ }
            }
            return _cache = [];
        }

        internal static string? Get(string key)
        {
            lock (Gate)
            {
                var d = Load();
                return d.TryGetValue(key, out var v) ? v : null;
            }
        }

        internal static void Set(string key, string value)
        {
            lock (Gate)
            {
                var d = new Dictionary<string, string>(Load()) { [key] = value };
                try
                {
                    Directory.CreateDirectory(Dir);
                    var tmp = FilePath + ".tmp";
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d,
                        new JsonSerializerOptions { WriteIndented = true }));
                    using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak", true);
                    else File.Move(tmp, FilePath);
                    _cache = d;
                }
                catch { /* Keep the last complete settings dictionary on publication failure. */ }
            }
        }
    }
}
