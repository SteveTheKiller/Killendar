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
            Assert.Equal("Import (Ctrl+I)", MainWindow.WithShortcut("Import", "Str_KS_Import"));
        }
    }
}
