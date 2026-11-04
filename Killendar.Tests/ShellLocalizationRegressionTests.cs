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
