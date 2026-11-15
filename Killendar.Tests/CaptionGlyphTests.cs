using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A 10px MDL2 X fills a 98SE caption button's 12x10 face edge to edge, so it reads
    /// as off center. The caption glyph size comes from the theme, and 98SE gets a smaller one.</summary>
    public sealed class CaptionGlyphTests
    {
        [Fact]
        public void ChromeButtonTakesItsGlyphSizeFromTheTheme()
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "Controls", "Controls.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var style = Assert.Single(xaml.Descendants(), e => e.Name.LocalName == "Style"
                && (string?)e.Attribute(x + "Key") == "ChromeButton");
            var size = Assert.Single(style.Elements(), e => e.Name.LocalName == "Setter"
                && (string?)e.Attribute("Property") == "FontSize");
            Assert.Equal("{DynamicResource CaptionGlyphSize}", (string?)size.Attribute("Value"));
        }

        [Fact]
        public void FlatCaptionGetsTheSmallerGlyph()
        {
            var source = File.ReadAllText(Path.Combine(Root(), "Services", "ThemeManager.cs"));
            Assert.Contains("combined[\"CaptionGlyphSize\"] = flat ? 8.0 : 10.0;", source);
        }

        private static string Root([CallerFilePath] string here = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
    }
}
