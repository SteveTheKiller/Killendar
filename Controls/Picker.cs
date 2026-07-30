using System;

namespace Killendar.Controls
{
    // Row and place models for FileDialog.
    public sealed class PickerPlace(string glyph, string label, string path)
    {
        public string Glyph { get; } = glyph;
        public string Label { get; } = label;
        public string Path  { get; } = path;
    }

    // One row in the folder pane: a subfolder or a (dimmed, non-pickable) file.
    public sealed class PickerEntry(string name, string fullPath, bool isFolder, long sizeBytes, DateTime modified)
    {
        private static readonly string GlyphFolder = ((char)0xE8B7).ToString();
        private static readonly string GlyphFile   = ((char)0xE8A5).ToString();

        public string   Name      { get; } = name;
        public string   FullPath  { get; } = fullPath;
        public bool     IsFolder  { get; } = isFolder;
        public long     SizeBytes { get; } = sizeBytes;
        public DateTime Modified  { get; } = modified;

        public string Glyph         => IsFolder ? GlyphFolder : GlyphFile;

        /// <summary>Shell icon, 16px, for the list and details rows. Cached by extension, so
        /// binding it per row is cheap.</summary>
        public System.Windows.Media.ImageSource? Icon
            => Services.ShellIcons.Small(FullPath, IsFolder);

        /// <summary>Shell icon, 32px, for the icon grid.</summary>
        public System.Windows.Media.ImageSource? IconLarge
            => Services.ShellIcons.Large(FullPath, IsFolder);

        public string SizeLabel     => IsFolder ? string.Empty : FormatSize(SizeBytes);
        public string ModifiedLabel => Modified == DateTime.MinValue ? string.Empty : Modified.ToString("yyyy-MM-dd HH:mm");

        private static string FormatSize(long b)
        {
            if (b < 1024) return b + " B";
            double kb = b / 1024.0;
            if (kb < 1024) return kb.ToString("0") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0") + " MB";
            return (mb / 1024.0).ToString("0.00") + " GB";
        }
    }
}
