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
    }
}
