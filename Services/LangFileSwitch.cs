using System;

namespace Killendar.Services
{
    /// <summary>
    /// Killendar.exe --lang-file &lt;path&gt; [calendar]: translator test mode. The switch is taken out
    /// of the command line before routing, so a calendar named after it still opens, and its value
    /// becomes <see cref="LocaleManager.ExternalFile"/>.
    /// </summary>
    internal static class LangFileSwitch
    {
        /// <summary>Splits --lang-file and its value out of a command line. Returns the value as
        /// given (null when the switch is absent or has no value) and the arguments left over.</summary>
        internal static (string? Path, string[] Remaining) Take(string[] args)
        {
            int index = Array.FindIndex(args, a => string.Equals(a, "--lang-file", StringComparison.OrdinalIgnoreCase));
            if (index < 0) return (null, args);
            string? path = index + 1 < args.Length ? args[index + 1] : null;
            int taken = path == null ? 1 : 2;
            var remaining = new string[args.Length - taken];
            Array.Copy(args, 0, remaining, 0, index);
            Array.Copy(args, index + taken, remaining, index, args.Length - index - taken);
            return (path, remaining);
        }
    }
}
