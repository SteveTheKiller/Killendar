using KillerFamily.Shell;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Tray Exit shows the window and closes it with a 150 ms fade. A click or key during
    /// that fade used to cancel the exit, so the fade's second close hid the window to the tray at
    /// opacity 0 and Open brought back an invisible window.</summary>
    public sealed class TrayExitFadeRegressionTests
    {
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.4)]
        [InlineData(0.99)]
        public void InputDuringTheExitFadeKeepsTheExit(double opacity)
            => Assert.False(CloseToTray.InputEndsExitRequest(opacity));

        [Fact]
        public void InputOnTheFullyShownWindowEndsTheExit()
            => Assert.True(CloseToTray.InputEndsExitRequest(1.0));
    }
}
