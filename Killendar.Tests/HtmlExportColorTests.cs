using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A category color from an edited or foreign .kcal can carry a trailing newline.
    /// The export must treat it as no color instead of failing the whole page.</summary>
    public sealed class HtmlExportColorTests
    {
        [Theory]
        [InlineData("#AABBCC", true)]
        [InlineData("#aabbcc", true)]
        [InlineData("#AABBCC\n", false)]
        [InlineData("#AABBC", false)]
        [InlineData(null, false)]
        public void OnlyAWholeHexColorPasses(string? hex, bool ok) => Assert.Equal(ok, HtmlExport.IsHex(hex));
    }
}
