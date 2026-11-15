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

        /// <summary>Win98 sized a caption button from its caption: height is the caption minus 4,
        /// width is that plus 2. The About band is 20px, so its X is 18x16, centered.</summary>
        [Fact]
        public void AboutCaptionCloseFitsTheTwentyPixelBand()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var main = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var close = Assert.Single(main.Descendants(), e => e.Name.LocalName == "Button"
                && (string?)e.Attribute("Margin") == "{DynamicResource AboutCaptionCloseMargin}");
            Assert.Equal("{DynamicResource AboutCaptionCloseWidth}", (string?)close.Attribute("Width"));
            Assert.Equal("{DynamicResource AboutCaptionCloseHeight}", (string?)close.Attribute("Height"));
            Assert.Equal("Center", (string?)close.Attribute("VerticalAlignment"));

            var theme = XDocument.Load(Path.Combine(Root(), "Themes", "App", "98SE.xaml"));
            string Val(string key) => Assert.Single(theme.Root!.Elements(),
                e => (string?)e.Attribute(x + "Key") == key).Value;
            Assert.Equal("18", Val("AboutCaptionCloseWidth"));
            Assert.Equal("16", Val("AboutCaptionCloseHeight"));
            Assert.Equal("0,0,0,0", Val("AboutCaptionCloseMargin"));
        }

        private static string Root([CallerFilePath] string here = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
    }
}
