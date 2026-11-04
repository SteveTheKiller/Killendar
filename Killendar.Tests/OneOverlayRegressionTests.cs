using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>One overlay at a time: F12 while the F1 keyboard map is up used to fade About in on
    /// top of it. Every F12 path goes through ShowAboutFromKey, which steps the map aside first, as
    /// the footer version already does.</summary>
    public sealed class OneOverlayRegressionTests
    {
        [Fact]
        public void F12NeverOpensAboutDirectly()
        {
            string shortcuts = File.ReadAllText(Path.Combine(Root(), "Shell", "Shortcuts.cs"));
            string outside = Regex.Replace(shortcuts, @"void ShowAboutFromKey\(\)\s*\{.*?\n        \}", "", RegexOptions.Singleline);
            int bare = Regex.Matches(outside, @"\bShowAboutOverlay\(\)").Count;
            Assert.True(bare == 0, $"Shortcuts.cs opens About without closing the keyboard map {bare} time(s).");
            Assert.Equal(2, Regex.Matches(shortcuts, @"\bShowAboutFromKey\(\);").Count);
        }

        [Fact]
        public void ShowAboutFromKeyClosesTheKeyboardMapFirst()
        {
            string shortcuts = File.ReadAllText(Path.Combine(Root(), "Shell", "Shortcuts.cs"));
            Match body = Regex.Match(shortcuts, @"void ShowAboutFromKey\(\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(body.Success, "Shortcuts.cs has no ShowAboutFromKey.");
            string text = body.Groups["body"].Value;
            int close = text.IndexOf("ShortcutsOverlay.Visibility = Visibility.Collapsed", StringComparison.Ordinal);
            int show = text.IndexOf("ShowAboutOverlay()", StringComparison.Ordinal);
            Assert.True(close >= 0 && show > close, "ShowAboutFromKey must collapse the keyboard map before it opens About.");
        }

        // From this file's own path, so the check works wherever the test build lands.
        private static string Root([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
            if (!File.Exists(Path.Combine(root, "Shell", "Shortcuts.cs")))
                throw new InvalidOperationException("Shell/Shortcuts.cs was not found beside the test project.");
            return root;
        }
    }
}
