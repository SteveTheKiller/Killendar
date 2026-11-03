using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace KillerFamily.Deployment
{
    internal static class InstalledImagePublication
    {
        internal static void Publish(string source, string target, Action register,
            Action<string, string>? copy = null, Action<string, string, string>? replace = null,
            Action<string>? verifySource = null)
        {
            source = Path.GetFullPath(source);
            target = Path.GetFullPath(target);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The installer source must be outside the installed image.");
            string token = Guid.NewGuid().ToString("N");
            string staged = target + ".install-" + token;
            string backup = target + ".previous-" + token;
            // Open the source once and keep it: no writer, rename or delete can slip in between the
            // trust check, the hash and the copy. Every later step reads this same handle.
            using var held = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            verifySource?.Invoke(source);
            byte[] expected = Hash(held);
            held.Position = 0;
            byte[]? original = File.Exists(target) ? Hash(target) : null;
            bool published = false;
            try
            {
                (copy ?? ((from, to) => CopyFlushed(held, to)))(source, staged);
                Verify(staged, expected);
                if (original != null)
                    (replace ?? ((from, to, previous) => File.Replace(from, to, previous)))(staged, target, backup);
                else File.Move(staged, target);
                published = true;
                Verify(target, expected);
                register();
            }
            catch (Exception failure)
            {
                try
                {
                    // Replace can move the old image before reporting failure. Recover from
                    // the verified backup even when it did not return successfully.
                    if (original != null && File.Exists(backup))
                    {
                        Verify(backup, original);
                        if (File.Exists(target))
                            File.Replace(backup, target, target + ".failed-" + token);
                        else File.Move(backup, target);
                        Verify(target, original);
                    }
                    else if (published && original == null && File.Exists(target))
                    {
                        Verify(target, expected);
                        File.Move(target, staged);
                    }
                }
                catch (Exception recovery)
                {
                    throw new IOException("Installation and image recovery failed. Retained recovery files: " +
                        staged + ", " + backup, new AggregateException(failure, recovery));
                }
                throw;
            }
            // Only a verified publication and successful registration permit backup cleanup.
            TryDelete(staged);
            TryDelete(backup);
        }

        private static void CopyFlushed(Stream input, string target)
        {
            input.Position = 0;
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(true);
        }

        private static byte[] Hash(string path)
        {
            using var input = File.OpenRead(path);
            return Hash(input);
        }

        private static byte[] Hash(Stream input)
        {
            using var hash = SHA256.Create();
            return hash.ComputeHash(input);
        }

        private static void Verify(string path, byte[] expected)
        {
            if (!Hash(path).SequenceEqual(expected))
                throw new IOException("The installed image did not match its verified source: " + path);
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
