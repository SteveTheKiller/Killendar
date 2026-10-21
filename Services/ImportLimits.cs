using System.IO;

namespace Killendar.Services
{
    /// <summary>Thrown when an import is larger or deeper than Killendar reads at once. Nothing
    /// has been written when it is thrown.</summary>
    public sealed class ImportLimitException : IOException
    {
        public ImportLimitException(string message) : base(message) { }
    }

    /// <summary>
    /// The most a single import reads. Measured on a Ryzen 5 3600 (Release, October 2026): a
    /// 27 MB file of 100,000 appointments parses in about 2 s and writes in about 11 s with a
    /// 605 MB peak working set, so these limits keep the worst accepted file within a cancelable
    /// wait and a bounded amount of memory. Settable for tests only.
    /// </summary>
    internal static class ImportLimits
    {
        internal static long MaxBytes = 50L * 1024 * 1024;
        internal static int MaxEvents = 100_000;
        internal static int MaxMimeDepth = 10;

        /// <summary>Refuses a file before any of it is read.</summary>
        internal static void CheckFile(string path)
        {
            if (new FileInfo(path).Length > MaxBytes)
                throw new ImportLimitException("The file is larger than " + MaxBytes / (1024 * 1024) + " MB.");
        }

        internal static void CheckCount(int events)
        {
            if (events > MaxEvents)
                throw new ImportLimitException("The file has more than " + MaxEvents + " appointments.");
        }
    }
}
