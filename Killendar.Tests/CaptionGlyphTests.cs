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
        /// width is that plus 2. Every dialog band and the About band are 20px on 98SE, so every
        /// dialog X takes the kit's DialogCloseWidth/Height, derived from the band.</summary>
        [Theory]
        [InlineData("Shell", "MainWindow.xaml")]
        [InlineData("Controls", "DialogTitleBar.xaml")]
        public void DialogCaptionCloseTakesTheBandDerivedSize(string folder, string file)
        {
            var xaml = XDocument.Load(Path.Combine(Root(), folder, file));
            var closes = xaml.Descendants().Where(e => e.Name.LocalName == "Button"
                && ((string?)e.Attribute("Style") == "{StaticResource ChromeCloseButton}"
                    || (file == "DialogTitleBar.xaml" && (string?)e.Attribute("Style") == "{StaticResource OverlayCloseButton}"))
                && (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) != "CloseBtn").ToList();
            Assert.NotEmpty(closes);
            foreach (var close in closes)
            {
                Assert.Equal("{DynamicResource DialogCloseWidth}", (string?)close.Attribute("Width"));
                Assert.Equal("{DynamicResource DialogCloseHeight}", (string?)close.Attribute("Height"));
                Assert.Equal("Center", (string?)close.Attribute("VerticalAlignment"));
            }
        }

        /// <summary>The dialogs take their card and caption from the shared DialogCard style and
        /// DialogTitleBar control, so no dialog carries its own copy that can drift.</summary>
        [Theory]
        [InlineData("CategoriesDialog.xaml")]
        [InlineData("KillendarsDialog.xaml")]
        [InlineData("PasswordDialog.xaml")]
        [InlineData("RemindersWindow.xaml")]
        [InlineData("SearchDialog.xaml")]
        [InlineData("QuickAddDialog.xaml")]
        [InlineData("FreeTimeDialog.xaml")]
        public void DialogUsesTheSharedCardAndTitleBar(string file)
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "Controls", file));
            Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "ContentControl"
                && (string?)e.Attribute("Style") == "{StaticResource DialogCard}");
            Assert.Single(xaml.Descendants(), e => e.Name.LocalName == "DialogTitleBar");
            Assert.DoesNotContain(xaml.Descendants(), e => e.Name.LocalName == "Button"
                && ((string?)e.Attribute("Style"))?.Contains("CloseButton") == true);
        }

        [Fact]
        public void FlatDialogCloseIsDerivedFromTheBand()
        {
            var source = File.ReadAllText(Path.Combine(Root(), "Services", "ThemeManager.cs"));
            Assert.Contains("SetIfAbsent(\"DialogCloseWidth\", pickerCaption ? bar - 2 : 28.0);", source);
            Assert.Contains("SetIfAbsent(\"DialogCloseHeight\", pickerCaption ? bar - 4 : 26.0);", source);
        }

        private static string Root([CallerFilePath] string here = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
    }
}
