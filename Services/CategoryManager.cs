using System;
using System.Collections.Generic;
using System.Windows.Media;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// The open Killendar's category definitions, cached for painting.
    ///
    /// Ported from KillerNotes' Tags.cs, which keeps the same split: the definitions (name plus
    /// color) live in the database so they travel inside the file, and an event's assignment is
    /// just a comma-separated list of names on the event itself. Nothing here is persisted - it
    /// is a lookup so a view can turn "Work" into a brush without a query per chip.
    ///
    /// Refresh() is called whenever the store reloads (open, unlock, switch Killendar, or a
    /// definition edit), which is cheap: a handful of rows.
    ///
    /// The brushes built here are deliberately literal SolidColorBrushes rather than
    /// SetResourceReference bindings, which is the opposite of the rule in CalendarChrome. That
    /// rule exists so THEME colors follow a theme switch; a category color is user data and must
    /// not change when the palette does.
    ///
    /// The one refinement to that rule (Steve, 2026-07-31): on the three single-hue themes, a
    /// SEEDED tag color that shares the theme's hue drowns in it - the green tag vanished into
    /// Greed. The stored color still never changes; the DISPLAYED color swaps to a same-family
    /// neighbor while such a theme is up. Same pattern and the same replacement hexes as
    /// KillerScan's per-theme device-type overrides. Only the seeded hexes are mapped: a custom
    /// color is the user's exact pick and stays exact.
    /// </summary>
    internal static class CategoryManager
    {
        /// <summary>Color for a name whose definition has been deleted. The assignment is left
        /// alone on purpose in that case, so the event still shows the category, in neutral gray
        /// rather than vanishing silently (KillerNotes' rule).</summary>
        internal const string OrphanHex = "#9A9A9A";

        private static Dictionary<string, string> _defs =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, SolidColorBrush> _brushes =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Definitions in the order the store returns them, for the pickers.</summary>
        internal static List<(string Name, string Color)> Order { get; private set; } =
            [];

        /// <summary>Raised when a live preview repoints a color, so the views can repaint without
        /// anything being written. Deliberately NOT raised by Refresh: Refresh runs inside
        /// EventStore.Open(), before the calendar views exist, and every other caller of it
        /// already raises EventStore.Changed straight afterwards.</summary>
        internal static event Action? Previewed;

        /// <summary>Points a category at a color WITHOUT touching the database, so the color
        /// picker can preview live on the calendar the way KillerNotes previews a group title.
        /// Nothing here persists: a Refresh, a commit, or the caller's own revert replaces it.</summary>
        internal static void Preview(string name, string hex)
        {
            _defs[name] = hex;
            _brushes.Remove(name);
            Previewed?.Invoke();
        }

        /// <summary>Re-reads the definitions from the open Killendar.</summary>
        internal static void Refresh(EventStore store)
        {
            Order = store.ListCategories();
            var defs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in Order) defs[def.Name] = def.Color;
            _defs = defs;
            // Colors can be re-pointed by an edit, so the brush cache cannot outlive a refresh.
            _brushes.Clear();
        }

        /// <summary>Hex for a defined category, or the orphan gray for one that is assigned to an
        /// event but no longer defined.</summary>
        internal static string HexOf(string name) =>
            _defs.TryGetValue(name, out string? hex) ? hex : OrphanHex;

        /// <summary>Frozen brush for a category name. Frozen because the same brush is handed to
        /// every chip in every view and never mutated.</summary>
        internal static SolidColorBrush BrushOf(string name)
        {
            if (_brushes.TryGetValue(name, out var cached)) return cached;
            var brush = new SolidColorBrush(ColorOf(name));
            brush.Freeze();
            _brushes[name] = brush;
            return brush;
        }

        internal static Color ColorOf(string name) => ParseHex(Displayed(HexOf(name)));

        // ── Theme-aware display (see the header) ────────────────────────────────
        // Lime on Greed, salmon on Blood, pale blue on Cyanotic - KillerScan's picks, proven on
        // the same panes. The neutral themes need no map: their surfaces are gray.
        private static readonly Dictionary<Theme, (string From, string To)[]> ThemeOverrides = new()
        {
            [Theme.Greed]    = [("#1EA54C", "#7BE06A")],
            [Theme.Blood]    = [("#DD504B", "#F08A5A")],
            [Theme.Cyanotic] = [("#50AEE8", "#8FC4FF")],
        };

        private static string Displayed(string hex)
        {
            if (ThemeOverrides.TryGetValue(ThemeManager.Current, out var map))
                foreach (var (from, to) in map)
                    if (string.Equals(hex, from, StringComparison.OrdinalIgnoreCase))
                        return to;
            return hex;
        }

        /// <summary>Theme switched: the display overrides just changed, so cached brushes are
        /// stale. The caller repaints the views afterwards (CalendarHost owns that order).</summary>
        internal static void OnThemeChanged() => _brushes.Clear();

        /// <summary>A malformed hex must not throw mid-render - a hand-edited database or a
        /// future format change lands here, and a gray chip is a better outcome than a crash.</summary>
        internal static Color ParseHex(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromRgb(0x9A, 0x9A, 0x9A); }
        }

        private static double Luminance(Color c) =>
            (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        /// <summary>Black on a pale category, white on a dark one, so the title reads on both.
        /// 0.55 is KillerNotes' threshold, kept identical so the two apps agree on borderline
        /// colors like the seeded yellow.</summary>
        internal static Brush ForegroundFor(Color c) =>
            Luminance(c) > 0.55 ? Brushes.Black : Brushes.White;

        /// <summary>The category that paints an event: the first one assigned. An event with no
        /// categories returns null and keeps the plain themed look.</summary>
        internal static string? PrimaryOf(CalendarEvent ev)
        {
            var names = EventStore.SplitCategories(ev.Categories);
            return names.Count > 0 ? names[0] : null;
        }

    }
}
