using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>
    /// Killendar has no status light in the footer corner, and at 4px its status text sat nearly
    /// against the window edge. FooterStatusInset gives it the 16px KillerNotes and KillerPDF use,
    /// and 98SE keeps its 4px beside the sunken cell's own padding.
    /// </summary>
    public sealed class FooterStatusInsetTests
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void RoundedThemesInsetTheStatusTextSixteenPixels()
            => Assert.Equal("16,0,0,0", Value(Path.Combine("Themes", "App", "Defaults.xaml"), "FooterStatusInset"));

        [Fact]
        public void The98SEStatusTextKeepsItsSpacing()
            => Assert.Equal("4,0,0,0", Value(Path.Combine("Themes", "App", "98SE.xaml"), "FooterStatusInset"));

        [Fact]
        public void TheFooterStatusTextTakesTheInset()
        {
            string window = File.ReadAllText(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            Match status = Regex.Match(window, @"<TextBlock x:Name=""StatusText""[^>]*>", RegexOptions.Singleline);
            Assert.True(status.Success, "MainWindow.xaml has no StatusText.");
            Assert.Contains(@"Margin=""{DynamicResource FooterStatusInset}""", status.Value);
            Assert.Contains(@"Padding=""{DynamicResource FooterCellPadding}""", status.Value);
        }

        private static string Value(string file, string key)
        {
            var element = XDocument.Load(Path.Combine(Root(), file)).Descendants()
                .SingleOrDefault(e => (string?)e.Attribute(Xaml + "Key") == key);
            Assert.True(element is not null, $"{file} does not declare {key}.");
            return element!.Value.Trim();
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
