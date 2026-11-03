using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    public sealed class ShortcutTooltipTests
    {
        [Theory]
        [InlineData("Str_KS_Import", "Ctrl+I")]
        [InlineData("Str_KS_Today", "T")]
        [InlineData("Str_KS_Month", "M or 1")]
        [InlineData("Str_KS_New", "N / Ctrl+N")]
        [InlineData("Str_KS_DeleteAppt", "Ctrl+Delete")]
        public void KeysComeFromTheShortcutTable(string labelKey, string keys)
        {
            Assert.Equal(keys, MainWindow.ShortcutKeys(labelKey));
        }

        [Fact]
        public void UnboundActionHasNoKeys()
        {
            Assert.Null(MainWindow.ShortcutKeys("Str_KS_NotABinding"));
            Assert.Equal("Categories", MainWindow.WithShortcut("Categories", "Str_KS_NotABinding"));
        }

        [Fact]
        public void TooltipEndsWithTheKeysInParentheses()
        {
            // An invisible marker sits before the bracket, so hiding hints works in any language.
            Assert.Equal("Import ​(Ctrl+I)", MainWindow.WithShortcut("Import", "Str_KS_Import"));
        }

        [Fact]
        public void HiddenHintsDropSingleKeyAndTranslatedSuffixes()
        {
            var converter = new KillerFamily.Layout.TooltipTextConverter();
            bool was = KillerFamily.Layout.ShortcutHints.Current.Enabled;
            try
            {
                KillerFamily.Layout.ShortcutHints.Current.Enabled = false;
                foreach (var (text, bare) in new[] { (KillerFamily.Layout.ShortcutHints.WithHint("Today", "T"), "Today"),
                                                     (KillerFamily.Layout.ShortcutHints.WithHint("Duplizieren", "Strg+D"), "Duplizieren") })
                    Assert.Equal(bare, converter.Convert(text, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture));
            }
            finally { KillerFamily.Layout.ShortcutHints.Current.Enabled = was; }
        }
    }
}
