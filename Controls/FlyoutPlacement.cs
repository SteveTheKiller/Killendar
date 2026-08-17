using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Killendar.Controls
{
    /// <summary>
    /// Every rail flyout opens in ONE place: the BOTTOM-LEFT CORNER OF THE CONTENT PANE.
    /// (2026-07-30, after this was got wrong repeatedly here and in KillerNotes)
    ///
    /// That corner is the answer because of what it avoids, and all three matter:
    ///   - it is INSIDE the window, so the flyout never hangs over the desktop;
    ///   - it is ABOVE the footer, so the status bar is never covered;
    ///   - it is RIGHT of the rail, so the rail icons are never covered.
    /// The content pane is the element that satisfies all three by definition - it is the region
    /// bounded by the rail on the left and the footer below - so the flyout is positioned against
    /// IT, not against the button, and not by any built-in placement mode.
    ///
    /// Why none of WPF's placement modes can do this: a Popup is its own top-level window, and the
    /// built-in modes only ever avoid the SCREEN edge. They do not know the app window exists, let
    /// alone the footer or the rail. "Right of the button" therefore opened over the desktop, and
    /// "Top" opened over the status bar. Only an explicit position against the pane works.
    ///
    /// The flyout content already carries its own margin for the drop shadow (14 on the theme
    /// popup, 6 on the menus), so pinning the popup flush to the corner leaves the VISIBLE card
    /// sitting neatly just inside it. No extra inset is added here.
    /// </summary>
    internal static class FlyoutPlacement
    {
        /// <summary>The content pane. Set once at startup; every flyout positions against it.</summary>
        private static FrameworkElement? _pane;

        internal static void UsePane(FrameworkElement pane) => _pane = pane;

        internal static void Attach(Popup popup, UIElement _)
        {
            popup.PlacementTarget = _pane;
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback =
                (popupSize, targetSize, __) => BottomLeftOfPane(popupSize, targetSize);
        }

        /// <summary>
        /// The export scope flyout's spot: the TOP-LEFT corner of the content pane - the same
        /// one-place rule as the rail flyouts' bottom-left, mirrored because its opener lives in
        /// the toolbar above the pane, so the flyout reads as dropping out of it. Same reasons
        /// apply: inside the window, clear of the toolbar, clear of the rail. (2026-07-31)
        /// </summary>
        internal static void AttachTopLeft(Popup popup)
        {
            popup.PlacementTarget = _pane;
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback =
                (_, _, _) => [new CustomPopupPlacement(new Point(0, 0), PopupPrimaryAxis.None)];
        }

        internal static void Attach(ContextMenu menu, UIElement _)
        {
            menu.PlacementTarget = _pane;
            menu.Placement = PlacementMode.Custom;
            // The shared ContextMenu style offsets ordinary pointer menus to compensate for
            // FlyoutCard's enlarged shadow halo. Rail menus use exact pane-corner coordinates,
            // so clear those offsets here and account for the halo below, as KillerPDF does.
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.CustomPopupPlacementCallback =
                (popupSize, targetSize, __) => BottomLeftOfPane(popupSize, targetSize);
        }

        /// <summary>
        /// Coordinates are relative to the placement target's top-left - the pane's top-left. So
        /// x = 0 is the pane's left edge (hard against the rail) and y = pane height - flyout
        /// height puts the flyout's bottom on the pane's bottom (hard against the footer).
        /// </summary>
        private static CustomPopupPlacement[] BottomLeftOfPane(Size popupSize, Size targetSize)
        {
            // FlyoutCard reserves 22px left, 18px top and 26px bottom for its shadow. Keep the
            // visible card six pixels inside the content pane while its invisible halo may extend.
            const double x = -16;
            double y = targetSize.Height - popupSize.Height + 20;

            // A flyout taller than the pane would otherwise start above it and run over the
            // toolbar; pin it to the pane's top instead and let it use the height it has.
            if (y < 0) y = 0;

            return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
        }
    }
}
