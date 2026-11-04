using System.Windows;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    public sealed class SidebarGrowFitTests
    {
        [Fact]
        public void WindowOnASecondMonitorStaysOnThatMonitor()
        {
            // Second monitor to the right of a 1920 px primary.
            var area = new Rect(1920, 0, 1920, 1040);

            var (grow, left) = MainWindow.SidebarGrowFit(area, left: 3500, width: 300, want: 260);

            Assert.Equal(260, grow);
            Assert.Equal(3280, left);
        }

        [Fact]
        public void GrowthIsCappedAtTheMonitorsWidth()
        {
            var area = new Rect(1920, 0, 1000, 1040);

            var (grow, left) = MainWindow.SidebarGrowFit(area, left: 1920, width: 900, want: 260);

            Assert.Equal(100, grow);
            Assert.Equal(1920, left);
        }

        // An Aero-snapped window reports WindowState.Normal, so the sidebar grow tells it apart by
        // its restore rectangle, which Windows keeps in workspace coordinates and leaves at the
        // pre-snap size while the window sits in its slot.
        [Fact]
        public void AWindowAtItsRestoreRectangleIsNotSnapped()
        {
            var monitor = new Rect(0, 0, 1366, 768);
            var work = new Rect(0, 0, 1366, 728);   // taskbar at the bottom
            Assert.False(MainWindow.IsSnapped(new Rect(100, 80, 800, 600), new Rect(100, 80, 800, 600), work, monitor));
        }

        [Fact]
        public void AWindowInALeftHalfSnapIsSnapped()
        {
            var monitor = new Rect(0, 0, 1366, 768);
            var work = new Rect(0, 0, 1366, 728);
            Assert.True(MainWindow.IsSnapped(new Rect(0, 0, 683, 728), new Rect(200, 100, 700, 500), work, monitor));
        }

        [Fact]
        public void ATopTaskbarShiftsTheRestoreRectangleWithoutLookingLikeASnap()
        {
            // Workspace coordinates start below a 40 px top taskbar: workspace (100, 60) is screen (100, 100).
            var monitor = new Rect(0, 0, 1920, 1080);
            var work = new Rect(0, 40, 1920, 1040);
            Assert.False(MainWindow.IsSnapped(new Rect(100, 100, 900, 700), new Rect(100, 60, 900, 700), work, monitor));
        }

        [Fact]
        public void AThirdWidthSnapOnASecondMonitorIsSnapped()
        {
            var monitor = new Rect(1920, 0, 1920, 1080);
            var work = new Rect(1920, 0, 1920, 1032);
            Assert.True(MainWindow.IsSnapped(new Rect(3200, 0, 640, 1032), new Rect(500, 120, 1000, 700), work, monitor));
        }
    }
}
