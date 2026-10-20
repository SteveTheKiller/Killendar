using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KillerFamily.Deployment
{
    internal static class InstalledFileCleanup
    {
        public static void Remove(string directory, params string[] files)
        {
            string[] paths = OwnedPaths(directory, files);
            foreach (string path in paths)
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            foreach (string folder in Folders(directory, paths))
                try
                {
                    if (!Linked(folder)) Directory.Delete(folder, recursive: false);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }

        public static string RemovalBatch(string directory, params string[] files)
        {
            string[] paths = OwnedPaths(directory, files);
            var script = new StringBuilder("setlocal DisableDelayedExpansion\r\n");
            foreach (string path in paths)
                script.Append("del /q \"").Append(path.Replace("%", "%%")).Append("\" >nul 2>&1\r\n");
            foreach (string folder in Folders(directory, paths))
                if (!Linked(folder))
                    script.Append("rmdir \"").Append(folder.Replace("%", "%%")).Append("\" >nul 2>&1\r\n");
            return script.ToString();
        }

        public static string[] OwnedPaths(string directory, params string[] files)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Length == 0 || root == Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar))
                throw new ArgumentException("An installation directory cannot be a volume root.");
            string prefix = root + Path.DirectorySeparatorChar;
            var result = new List<string>();
            foreach (string file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file) || file.IndexOfAny(new[] { ':', '*', '?', '"', '\r', '\n' }) >= 0)
                    throw new ArgumentException("Installed files must be relative literal paths.");
                string path = Path.GetFullPath(Path.Combine(prefix, file));
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Installed file is outside the installation directory.");
                bool linked = false;
                for (string? parent = Path.GetDirectoryName(path); parent != null && parent.Length >= root.Length;
                     parent = Path.GetDirectoryName(parent))
                    if (Linked(parent)) { linked = true; break; }
                if (!linked) result.Add(path);
            }
            return result.ToArray();
        }

        private static bool Linked(string directory)
            => Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;

        private static IEnumerable<string> Folders(string directory, string[] paths)
            => paths.Select(Path.GetDirectoryName).Where(path => path != null).Cast<string>()
                .Append(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(path => path.Length);
    }
}
