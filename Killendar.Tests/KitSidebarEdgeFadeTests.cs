using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GC-007: scrolling inside the same fade state keeps one mask brush and does not walk
    /// the tree again.</summary>
    [Collection("Calendar settings")]
    public sealed class KitSidebarEdgeFadeTests
    {
        [Fact]
        public void ScrollingWithinOneFadeStateKeepsTheMaskBrush() => KitTestHost.Sta(() =>
        {
            var scroll = new ScrollViewer
            {
                Height = 100,
                Width = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Height = 1000, Width = 50 },
            };
            var fade = KitTestHost.AppType("Killendar.Controls.SidebarEdgeFade");
            KitTestHost.CallStatic(fade, "Attach", scroll);
            // Layout only: no window is created or shown.
            scroll.Measure(new Size(100, 100));
            scroll.Arrange(new Rect(0, 0, 100, 100));
            scroll.UpdateLayout();

            scroll.ScrollToVerticalOffset(10);
            scroll.UpdateLayout();
            var viewport = (ScrollContentPresenter)scroll.Template.FindName("PART_ScrollContentPresenter", scroll);
            var first = viewport.OpacityMask;
            Assert.NotNull(first);

            scroll.ScrollToVerticalOffset(20);
            scroll.UpdateLayout();

            Assert.Same(first, viewport.OpacityMask);
        });
    }
}
