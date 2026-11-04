using System.Windows;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The themed system menu's card sits inside a transparent halo for its shadow (the
    /// MenuRoot margin, 22,18,22,26). The popup's origin is the halo's corner, so the visible card
    /// lands at the pointer only when the halo is taken off, as FlyoutPlacement does for every
    /// other menu.</summary>
    public sealed class SystemMenuPlacementTests
    {
        [Fact]
        public void TheVisibleCardStartsAtThePointer()
        {
            Point origin = MainWindow.SystemMenuOrigin(new Point(500, 300), new Thickness(22, 18, 22, 26));
            Assert.Equal(478, origin.X);
            Assert.Equal(282, origin.Y);
        }

        [Fact]
        public void NoHaloLeavesThePointAlone()
            => Assert.Equal(new Point(40, 60), MainWindow.SystemMenuOrigin(new Point(40, 60), new Thickness(0)));
    }
}
