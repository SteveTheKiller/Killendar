using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The keyboard map lights every key a binding answers to. Alt+Left and Alt+Right move
    /// an appointment a day just as Alt+Up and Alt+Down move it 15 minutes (Shortcuts.cs), so the
    /// ALT layer lights all four arrows.</summary>
    public sealed class KeyboardMapLayerTests
    {
        [Theory]
        [InlineData("Up")]
        [InlineData("Down")]
        [InlineData("Left")]
        [InlineData("Right")]
        public void AltLayerLightsEveryNudgeArrow(string cap)
            => Assert.Contains(cap, MainWindow.ShortcutCaps("Alt+Arrows"));
    }
}
