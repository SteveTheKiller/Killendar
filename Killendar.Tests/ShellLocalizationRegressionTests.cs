using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Text the shell builds in code follows a language switch.</summary>
    public sealed class ShellLocalizationRegressionTests
    {
        /// <summary>The density button's tooltip is built in code (Density.cs), so the language
        /// switch rebuilds it with the other code-built tooltips.</summary>
        [Fact]
        public void LanguageSwitchRebuildsTheDensityTooltip()
        {
            string language = File.ReadAllText(Path.Combine(Root(), "Shell", "Language.Extras.cs"));
            Match body = Regex.Match(language, @"partial void RelocalizeAppUi\(\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(body.Success, "Language.Extras.cs has no RelocalizeAppUi.");
            Assert.Contains("RefreshDensityTooltip();", body.Groups["body"].Value);
        }

        /// <summary>The app-size readout is worded in the language, not a hardcoded English "UI".</summary>
        [Fact]
        public void AppSizeReadoutUsesTheLocalizedWording()
            => Assert.Equal("Velikost aplikacije 120%", Killendar.Shell.AppScaleReadout.Text("Velikost aplikacije {0}%", 1.2));

        [Fact]
        public void AppSizeReadoutFallsBackToTheFamilyWording()
            => Assert.Equal("App size 90%", Killendar.Shell.AppScaleReadout.Text(null, 0.9));

        /// <summary>When the readout times out it gives the status line back, unless something newer
        /// (a save, an import) has written there in the meantime.</summary>
        [Fact]
        public void ReadoutTimeoutKeepsANewerStatusLine()
            => Assert.Null(Killendar.Shell.AppScaleReadout.Restore("Appointment saved", "App size 120%", "Ready", "Ready"));

        [Fact]
        public void ReadoutTimeoutRestoresTheEarlierStatusLine()
            => Assert.Equal("Imported 3 appointments",
                Killendar.Shell.AppScaleReadout.Restore("App size 120%", "App size 120%", "Imported 3 appointments", "Ready"));

        [Fact]
        public void ReadoutTimeoutWithNoEarlierLineShowsReady()
            => Assert.Equal("Ready", Killendar.Shell.AppScaleReadout.Restore("App size 120%", "App size 120%", null, "Ready"));

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
