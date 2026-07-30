using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Killendar.Controls
{
    /// <summary>
    /// The procedural film grain the whole app is textured with. One bitmap, generated once and
    /// shared by every grain brush, so the pattern lines up across the title bar, the panes and the
    /// menus instead of each surface getting its own noise.
    ///
    /// The specks are deliberately a mix of bright AND dark: a bright-only grain disappears on the
    /// light themes and a dark-only one disappears on the dark ones. The seed is fixed so a rebuild
    /// produces the identical pattern.
    /// </summary>
    internal static class GrainTexture
    {
        private const int Size = 256;
        private const int Seed = 1337;

        /// <summary>Generates the tile. Frozen, so it can be shared across brushes and threads.</summary>
        internal static BitmapSource Generate()
        {
            var bmp = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[Size * Size * 4];   // starts fully transparent
            var rng = new Random(Seed);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (rng.Next(3) != 0) continue;                 // ~33% density
                bool bright = rng.Next(2) == 0;
                byte v = bright ? (byte)rng.Next(190, 255) : (byte)rng.Next(0, 50);
                pixels[i] = v;
                pixels[i + 1] = v;
                pixels[i + 2] = v;
                pixels[i + 3] = (byte)rng.Next(35, 95);         // alpha keeps it subtle
            }

            bmp.WritePixels(new Int32Rect(0, 0, Size, Size), pixels, Size * 4, 0);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Paints the tile into every named ImageBrush the window happens to declare (all optional),
        /// and replaces the keyed GrainTileBrush resource used by menus and flyouts.
        /// </summary>
        internal static void Apply(FrameworkElement scope, params string[] brushNames)
        {
            var bmp = Generate();

            foreach (var name in brushNames)
                if (scope.FindName(name) is ImageBrush ib) ib.ImageSource = bmp;

            // The keyed resource brush is frozen, so its ImageSource cannot be set in place. Swap in
            // a fresh frozen brush instead; DynamicResource consumers re-resolve automatically.
            var tile = new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, Size, Size),
                Stretch = Stretch.None
            };
            tile.Freeze();
            Application.Current.Resources["GrainTileBrush"] = tile;
        }
    }
}
