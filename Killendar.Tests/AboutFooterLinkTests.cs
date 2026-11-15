using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Copy diagnostics sits in the About card the way KillerShell's Clear all Data does:
    /// a small dim mono link at the right end of the settings, not an outlined button under them.</summary>
    public sealed class AboutFooterLinkTests
    {
        [Fact]
        public void CopyDiagnosticsIsARightAlignedFooterLink()
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            Assert.DoesNotContain(xaml.Descendants(), e => e.Name.LocalName == "Button"
                && (string?)e.Attribute("Click") == "AboutDiagnostics_Click");

            var link = Assert.Single(xaml.Descendants(), e => e.Name.LocalName == "Hyperlink"
                && (string?)e.Attribute("Click") == "AboutDiagnostics_Click");
            var host = link.Parent!;
            Assert.Equal("TextBlock", host.Name.LocalName);
            Assert.Equal("{DynamicResource MonoFont}", (string?)host.Attribute("FontFamily"));
            Assert.Equal("10", (string?)host.Attribute("FontSize"));
            Assert.Equal("Right", (string?)host.Attribute("HorizontalAlignment"));
            Assert.Equal("None", (string?)link.Attribute("TextDecorations"));
            Assert.Contains("DimTextBrush", link.ToString());
        }

        /// <summary>The Photon note rides on the checkbox's tooltip, so no extra line pushes the
        /// right column taller than the left and the settings rows line up.</summary>
        [Fact]
        public void PhotonNoteIsTheCheckboxTooltip()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var xaml = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var toggle = Assert.Single(xaml.Descendants(), e => (string?)e.Attribute(x + "Name") == "PhotonLookupToggle");
            Assert.Equal("{DynamicResource Str_About_PhotonPrivacy}", (string?)toggle.Attribute("ToolTip"));
            Assert.DoesNotContain(xaml.Descendants(), e => e.Name.LocalName == "TextBlock"
                && (string?)e.Attribute("Text") == "{DynamicResource Str_About_PhotonPrivacy}");
        }

        private static string Root([CallerFilePath] string here = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
    }
}
