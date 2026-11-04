using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The About card traps focus, so the version line that opens the release notes must be
    /// a keyboard stop with an Invoke action. A hyperlink is both, and it is how the card already
    /// links thekiller.net and Killer Tools.</summary>
    public sealed class AboutVersionKeyboardTests
    {
        private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void TheVersionLineIsAHyperlinkThatOpensTheReleaseNotes()
        {
            var window = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var version = window.Descendants().SingleOrDefault(e => (string?)e.Attribute(Xaml + "Name") == "AboutVersionBlock");
            Assert.True(version is not null, "MainWindow.xaml has no AboutVersionBlock.");
            var link = version!.Ancestors(Wpf + "Hyperlink").FirstOrDefault();
            Assert.True(link is not null, "The About version line is not inside a Hyperlink, so the keyboard cannot reach it.");
            Assert.Equal("AboutVersionLink_Click", (string?)link!.Attribute("Click"));
            Assert.Null(version.Attribute("MouseLeftButtonUp"));
        }

        // From this file's own path, so the check works wherever the test build lands.
        private static string Root([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
            if (!File.Exists(Path.Combine(root, "Shell", "MainWindow.xaml")))
                throw new InvalidOperationException("Shell/MainWindow.xaml was not found beside the test project.");
            return root;
        }
    }
}
