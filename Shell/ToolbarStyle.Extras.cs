using System.Collections.Generic;
using System.Windows.Controls;

namespace Killendar.Shell
{
    /// <summary>
    /// APP-OWNED: Killendar's side of the family toolbar (Shell/ToolbarStyle.cs): the buttons on
    /// the bar, the shortcut each tooltip carries, and the Month arrow and overflow menu that
    /// follow every restyle.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// The bar, in order. Labels reuse the existing Str_Btn_* / Str_View_* keys rather than
        /// inventing a parallel set, so a caption can never drift from the button it names.
        ///
        /// Prev and Next are deliberately absent. They are arrows bracketing the date label, not
        /// commands, and hanging "Previous" / "Next" captions off them breaks the
        /// `&lt; Today July 2026 ... &gt;` composition the toolbar layout is built around.
        ///
        /// GLYPHS: this table is the ONLY place a toolbar codepoint appears. The overflow menu
        /// reads it too (ToolbarOverflow.GlyphFor), because it used to carry its own copies and
        /// they drifted the moment one changed.
        ///
        /// Segoe MDL2 cannot be rasterised on the box this was written on, so every codepoint was
        /// checked against a real build. ALL OF THEM ARE NOW CONFIRMED - do not re-raise them.
        /// The two that were wrong came from trusting a glyph's DOCUMENTED name instead of looking
        /// at it: E736 is listed as GridView and draws an open book, and E8DA / E8E5 mean import
        /// and export but render as two nearly identical pages, so the pair read as one button
        /// drawn twice. That is the lesson, not the glyphs.
        /// </summary>
        private IEnumerable<(Button Btn, int Glyph, string LabelKey)> ToolbarItems()
        {
            yield return (NewEventBtn, 0xE710, "Str_Btn_New");      // Add
            // E8B5 / EDE1, picked from a build. The old E8DA / E8E5 pair was the
            // arrow-into-page and arrow-out-of-page glyphs - correct in meaning, but they render as
            // two nearly identical pages and read as one button drawn twice.
            yield return (ImportBtn,   0xE8B5, "Str_Btn_Import");   // Import
            yield return (ExportBtn,   0xEDE1, "Str_Btn_Export");   // Export
            yield return (DeletedBtn,  0xE74D, "Str_Btn_Deleted");  // Delete (trash can), not yet confirmed from a build
            yield return (SearchBtn,   0xE721, "Str_Btn_Search");   // Search (magnifier), not yet confirmed from a build
            // E787 is the calendar - confirmed from a build that it renders as one, so it
            // belongs on MONTH. It was on Today, and E736 (guessed as GridView) was on Month and
            // actually draws an open book. Today moved to E8D1. (2026-07-30, both picked
            // by looking at a real render rather than trusting the glyph docs - the rule this
            // file got wrong twice.)
            yield return (TodayBtn,    0xE8D1, "Str_Btn_Today");    // Today
            yield return (TabMonth,    0xE787, "Str_View_Month");   // Calendar          (confirmed)
            yield return (TabWeek,     0xE8C0, "Str_View_Week");    // CalendarWeek      (confirmed)
            yield return (TabDay,      0xE8BF, "Str_View_Day");     // CalendarDay       (confirmed)
            yield return (TabAgenda,   0xE8FD, "Str_View_Agenda");  // List              (confirmed)
        }

        /// <summary>The overlay label key whose shortcut a toolbar button carries (ShortcutsOverlay.cs).</summary>
        private string ToolbarShortcutKey(Button btn)
        {
            if (ReferenceEquals(btn, NewEventBtn)) return "Str_KS_New";
            if (ReferenceEquals(btn, ImportBtn))   return "Str_KS_Import";
            if (ReferenceEquals(btn, ExportBtn))   return "Str_KS_Export";
            if (ReferenceEquals(btn, TodayBtn))    return "Str_KS_Today";
            if (ReferenceEquals(btn, TabMonth))    return "Str_KS_Month";
            if (ReferenceEquals(btn, TabWeek))     return "Str_KS_Week";
            if (ReferenceEquals(btn, TabDay))      return "Str_KS_Day";
            if (ReferenceEquals(btn, TabAgenda))   return "Str_KS_Agenda";
            return "";
        }

        // The name always stays reachable, and the tooltip carries the button's shortcut.
        partial void ToolbarTooltip(Button btn, string label, ref object tip)
            => tip = WithShortcut(label, ToolbarShortcutKey(btn));

        partial void OnToolbarStyleApplied()
        {
            // The Month dropdown arrow is not a toolbar item, but it must stand as tall as the
            // Month button it belongs to in every size and text mode.
            MonthModeBtn.Height = TabMonth.Height;
            // Either change alters every button's width, so the cached widths are stale: drop
            // them, not just re-test. Also re-read the captions the overflow menu copies.
            RefreshViewOverflowMenu();
            InvalidateToolbarOverflow();
        }
    }
}
