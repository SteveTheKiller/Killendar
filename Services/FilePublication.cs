using System;
using System.IO;
using System.Text;

namespace Killendar.Services
{
    /// <summary>
    /// Writes a user-chosen output file without ever exposing a partial one. The complete content
    /// goes to an owned sibling file first, is flushed to disk, and only then replaces the
    /// destination. Any failure before that swap leaves the previous file exactly as it was and
    /// removes the stage.
    ///
    /// The stage sits beside the destination, not in %TEMP%, so the final step is a same-volume
    /// File.Replace that also keeps the destination's ACL and attributes.
    /// </summary>
    internal static class FilePublication
    {
        /// <summary>Test seam: runs with the flushed stage path just before it is published.</summary>
        internal static Action<string>? AfterStaged;

        internal static void WriteText(string path, string text, Encoding encoding) =>
            Write(path, stream =>
            {
                byte[] preamble = encoding.GetPreamble();
                stream.Write(preamble, 0, preamble.Length);
                byte[] bytes = encoding.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            });

        internal static void Write(string path, Action<Stream> write)
        {
            path = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(path)!;
            string stage = Path.Combine(directory,
                "~" + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(true);
                }
                AfterStaged?.Invoke(stage);
                if (File.Exists(path)) File.Replace(stage, path, null);
                else File.Move(stage, path);
            }
            finally
            {
                try { if (File.Exists(stage)) File.Delete(stage); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
