using Xunit;

namespace Killendar.Tests
{
    /// <summary>GL-007: a second click on the date settings button closes the flyout instead of reopening it.</summary>
    public sealed class DateSettingsToggleTests
    {
        private static string Decide(bool isOpen, long sinceClosedMs)
        {
            var menu = typeof(App).Assembly.GetType("Killendar.Controls.DateSettingsMenu")!;
            return KitTestHost.CallStatic(menu, "Decide", isOpen, sinceClosedMs)!.ToString()!;
        }

        [Fact]
        public void AnOpenFlyoutIsClosedByTheNextClick() => Assert.Equal("Close", Decide(true, long.MaxValue));

        [Fact]
        public void ALateClickThatFollowsTheOutsideClickCloseDoesNotReopen() => Assert.Equal("Ignore", Decide(false, 40));

        [Fact]
        public void AClosedFlyoutOpensOnAClick() => Assert.Equal("Open", Decide(false, 5000));
    }
}
