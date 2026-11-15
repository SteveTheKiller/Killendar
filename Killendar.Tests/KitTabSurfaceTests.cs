using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GC-004 and GC-005: the tab surface keeps its clip geometry and blend brush graph
    /// instead of rebuilding them on every arrange pass and every animation frame.</summary>
    [Collection("Calendar settings")]
    public sealed class KitTabSurfaceTests
    {
        [Fact]
        public void AnUnchangedArrangeKeepsTheSameClipGeometry() => KitTestHost.Sta(() =>
        {
            var child = new Border { Width = 40, Height = 20 };
            var tab = new TabSurfaceBorder
            {
                Child = child,
                BorderThickness = new Thickness(1, 3, 1, 0),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Padding = new Thickness(4),
            };
            tab.Measure(new Size(200, 60));
            tab.Arrange(new Rect(0, 0, 200, 60));
            var first = child.Clip;
            Assert.NotNull(first);

            tab.InvalidateArrange();
            tab.Arrange(new Rect(0, 0, 200, 60));

            Assert.Same(first, child.Clip);
        });

        [Fact]
        public void ABlendFrameReusesTheBrushGraph() => KitTestHost.Sta(() =>
        {
            var tab = new TabSurfaceBorder
            {
                MenuBackground = new SolidColorBrush(Colors.Gray),
                ContentBackground = new SolidColorBrush(Colors.White),
            };
            tab.BlendProgress = 0.2;
            var first = tab.Background;
            tab.BlendProgress = 0.3;
            Assert.Same(first, tab.Background);
            Assert.Equal(0.3, ((DrawingGroup)((DrawingBrush)tab.Background).Drawing).Children[1] is DrawingGroup g ? g.Opacity : -1, 3);
        });
    }
}
