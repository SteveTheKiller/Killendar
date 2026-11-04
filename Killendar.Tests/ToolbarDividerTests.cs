using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A toolbar divider only ever stands between two groups that are showing. When the
    /// file buttons shed into "..." their dividers go with them instead of doubling up beside New.</summary>
    public sealed class ToolbarDividerTests
    {
        [Theory]
        [InlineData(true, true, true, true)]     // New | file | nav
        [InlineData(true, false, false, true)]   // New | nav
        [InlineData(false, true, false, true)]   // file | nav
        [InlineData(false, false, false, false)] // nav alone
        public void DividersOnlySeparateGroupsThatAreShowing(bool newShown, bool fileShown, bool afterNew, bool beforeNav)
        {
            Assert.Equal((afterNew, beforeNav), MainWindow.ToolbarDividers(newShown, fileShown));
        }
    }
}
