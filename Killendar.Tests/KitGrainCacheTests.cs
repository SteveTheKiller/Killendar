using System;
using System.Windows.Media.Imaging;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GC-008: the generated grain tiles are cached per accent, but the cache is bounded.</summary>
    [Collection("Calendar settings")]
    public sealed class KitGrainCacheTests
    {
        [Fact]
        public void TheTileCacheStaysBounded() => KitTestHost.Sta(() =>
        {
            var grain = KitTestHost.AppType("Killendar.Controls.GrainTexture");
            Func<BitmapSource> make = () => BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, new byte[1], 1);
            for (int key = 1000; key < 1100; key++)
                KitTestHost.CallStatic(grain, "Cached", key, make);
            var count = (int)grain.GetProperty("CacheCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.GetValue(null)!;
            Assert.True(count <= 16, "The grain cache held " + count + " tiles.");
        });
    }
}
