using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Every toolbar button that runs a bound action shows its keys, Search included.</summary>
    public sealed class ToolbarShortcutHintTests
    {
        [Theory]
        [InlineData("NewEventBtn", "Str_KS_New")]
        [InlineData("ImportBtn", "Str_KS_Import")]
        [InlineData("SearchBtn", "Str_KS_Search")]
        [InlineData("TabAgenda", "Str_KS_Agenda")]
        public void ToolbarButtonsCarryTheirBinding(string button, string labelKey)
            => Assert.Equal(labelKey, MainWindow.ToolbarShortcutKey(button));

        [Fact]
        public void SearchTooltipEndsWithCtrlF()
            => Assert.EndsWith("(Ctrl+F)", MainWindow.WithShortcut("Search", MainWindow.ToolbarShortcutKey("SearchBtn")));

        [Fact]
        public void DeletedHasNoShortcut()
            => Assert.Equal("", MainWindow.ToolbarShortcutKey("DeletedBtn"));
    }
}
