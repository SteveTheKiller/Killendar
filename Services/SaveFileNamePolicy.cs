using System;
using System.IO;

namespace Killendar.Services;

internal static class SaveFileNamePolicy
{
    internal static string ApplyExtension(string path, string? extension, bool addExtension, bool requireExtension)
    {
        if (!addExtension || string.IsNullOrWhiteSpace(extension)) return path;

        string normalized = extension!.StartsWith(".") ? extension : "." + extension;
        string current = Path.GetExtension(path);
        if (current.Equals(normalized, StringComparison.OrdinalIgnoreCase)) return path;
        // A name typed as "report." already carries the dot, so it must not turn into "report..ics".
        if (current.Length == 0 || requireExtension) return path.TrimEnd('.') + normalized;
        return path;
    }
}
