using Killendar.Services;
using System;
using System.IO;
using Xunit;

namespace Killendar.Tests
{
    // A Uri built from a DOS path reads '#' as a fragment and a valid %xx as an escape, so the
    // picker's thumbnails silently fell back to the type icon for those names.
    public sealed class PickerSpecialNameRegressionTests
    {
        private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        [Theory]
        [InlineData("plain.png")]
        [InlineData("photo #3.png")]
        [InlineData("a%20b.png")]
        public void ThumbnailDecodesWhateverTheFileName(string name)
        {
            string folder = Path.Combine(TestScratch.Root, "thumbs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, name);
                File.WriteAllBytes(path, Convert.FromBase64String(OnePixelPng));
                Assert.NotNull(ShellIcons.Thumbnail(path, 16));
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
