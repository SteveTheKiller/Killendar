using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// ============================================================
// Toolbar appearance: TWO INDEPENDENT SETTINGS - icon size, and where the text goes.
//
// It used to be one five-way enum (SmallIcons | LargeIcons | TextBeside | TextUnder | TextOnly),
// copied from KillerPDF. That shape cannot express "large icons WITH text" or "small icons with
// text", because choosing any text option threw the size choice away - the two were never
// really one axis, they only looked like one. (Steve, 2026-07-30.)
//
//   icon size:  Small | Large
//   text:       None | Beside | Under | Only
//
// Every combination is legal. "Only" is the one that ignores the size, because there is no icon
// to size - the picker greys the size options out rather than hiding them, so the setting is
// still visibly there and comes back when text moves off Only.
//
// Where the picker lives: right-click the toolbar. KillerPDF puts it in a settings panel and
// Killendar has no such panel, and a right-click menu on a toolbar is the Windows convention
// anyway (Steve, 2026-07-30).
//
// Two things that will break this if changed carelessly:
//   * The view buttons carry their view name in Tag ("Month", "Week", ...) and ViewTab_Click
//     reads it. Never write to Tag from here.
//   * The active view is highlighted by setting the BUTTON's Foreground (CalendarHost.cs). The
//     content built below therefore must NOT set Foreground on its own TextBlocks, or the
//     highlight stops reaching them and every view button looks inactive.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private enum IconSize { Small, Large }
        private enum LabelMode { None, Beside, Under, Only }

        // Large icons with the text underneath, by default (Steve, 2026-07-30: "I think it looks
        // best"). It is also the combination that only exists because the two axes were split -
        // the old five-way enum had a TextUnder mode but no way to say it was the LARGE icons
        // underneath, so this exact bar could not be reached at all.
        private IconSize _iconSize = IconSize.Large;
        private LabelMode _labelMode = LabelMode.Under;

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
            // E8B5 / EDE1, Steve's picks from a build. The old E8DA / E8E5 pair was the
            // arrow-into-page and arrow-out-of-page glyphs - correct in meaning, but they render as
            // two nearly identical pages and read as one button drawn twice.
            yield return (ImportBtn,   0xE8B5, "Str_Btn_Import");   // Import            (Steve's pick)
            yield return (ExportBtn,   0xEDE1, "Str_Btn_Export");   // Export            (Steve's pick)
            // E787 is the calendar - Steve confirmed from a build that it renders as one, so it
            // belongs on MONTH. It was on Today, and E736 (guessed as GridView) was on Month and
            // actually draws an open book. Today moved to E8D1. (Steve, 2026-07-30, both picked
            // by looking at a real render rather than trusting the glyph docs - the rule this
            // file got wrong twice.)
            yield return (TodayBtn,    0xE8D1, "Str_Btn_Today");    // Today             (Steve's pick)
            yield return (TabMonth,    0xE787, "Str_View_Month");   // Calendar          (confirmed)
            yield return (TabWeek,     0xE8C0, "Str_View_Week");    // CalendarWeek      (confirmed)
            yield return (TabDay,      0xE8BF, "Str_View_Day");     // CalendarDay       (confirmed)
            yield return (TabAgenda,   0xE8FD, "Str_View_Agenda");  // List              (confirmed)
        }

        private static readonly (IconSize Size, string LabelKey)[] IconSizes =
        [
            (IconSize.Small, "Str_Toolbar_SmallIcons"),
            (IconSize.Large, "Str_Toolbar_LargeIcons"),
        ];

        private static readonly (LabelMode Mode, string LabelKey)[] LabelModes =
        [
            (LabelMode.None,   "Str_Toolbar_TextNone"),
            (LabelMode.Beside, "Str_Toolbar_TextBeside"),
            (LabelMode.Under,  "Str_Toolbar_TextUnder"),
            (LabelMode.Only,   "Str_Toolbar_TextOnly"),
        ];

        private static readonly FontFamily MdlFont = new("Segoe MDL2 Assets");

        // ---- apply ----

        /// <summary>
        /// Restores the saved settings. Call once at startup, after the locale is in place - the
        /// captions are read through Loc() and would otherwise be built in the wrong language.
        /// </summary>
        private void InitToolbarStyle()
        {
            if (Settings.Get("ToolbarIconSize") is string sz && Enum.TryParse<IconSize>(sz, out var s))
                _iconSize = s;
            if (Settings.Get("ToolbarLabels") is string lb && Enum.TryParse<LabelMode>(lb, out var l))
                _labelMode = l;
            else MigrateOldToolbarStyle();

            BuildToolbarMenu();
            ApplyToolbarStyle();
        }

        /// <summary>
        /// Reads the retired single "ToolbarStyle" setting and splits it across the two new ones,
        /// so an existing install keeps the bar it was left on instead of silently resetting.
        /// Only runs when the new keys are absent.
        /// </summary>
        private void MigrateOldToolbarStyle()
        {
            switch (Settings.Get("ToolbarStyle"))
            {
                case "SmallIcons": _iconSize = IconSize.Small; _labelMode = LabelMode.None;   break;
                case "LargeIcons": _iconSize = IconSize.Large; _labelMode = LabelMode.None;   break;
                // The old text modes never said what size the icon was, so they take the default.
                case "TextBeside": _labelMode = LabelMode.Beside; break;
                case "TextUnder":  _labelMode = LabelMode.Under;  break;
                case "TextOnly":   _labelMode = LabelMode.Only;   break;
            }
        }

        private void SetIconSize(IconSize size)  { _iconSize = size;  Settings.Set("ToolbarIconSize", size.ToString()); ApplyToolbarStyle(); }
        private void SetLabelMode(LabelMode mode) { _labelMode = mode; Settings.Set("ToolbarLabels", mode.ToString()); ApplyToolbarStyle(); }

        private void ApplyToolbarStyle()
        {
            foreach (var (btn, glyph, key) in ToolbarItems())
            {
                var label = Loc(key);
                btn.Content = BuildToolbarContent(_iconSize, _labelMode, glyph, label);
                SizeToolbarButton(btn, _iconSize, _labelMode);
                // The name always stays reachable, which is what makes an icon-only bar usable.
                btn.ToolTip = label;
            }

            foreach (var item in ToolbarMenu.Items)
            {
                if (item is not MenuItem mi) continue;
                if (mi.Tag is IconSize szTag)
                {
                    mi.IsChecked = szTag == _iconSize;
                    // Text-only has no icon to size. Grey the choice rather than hiding it, so the
                    // setting stays visible and comes back when text moves off Only.
                    mi.IsEnabled = _labelMode != LabelMode.Only;
                }
                else if (mi.Tag is LabelMode lbTag) mi.IsChecked = lbTag == _labelMode;
            }

            // Either change alters every button's width, so the cached widths are stale - drop
            // them, not just re-test. Also re-read the captions the overflow menu copies.
            RefreshViewOverflowMenu();
            InvalidateToolbarOverflow();
        }

        // ---- KillerPDF's numbers, from SettingsPanel.cs SetToolbarButton. Do not retune these.
        //
        // Killendar was drawing its icons at 14/20 with 9,4 padding and no explicit button size,
        // so the same bar came out visibly smaller and tighter than KillerPDF's. These are the
        // values that were actually settled on there. (Steve, 2026-07-30: "PLEASE MAKE THE ICONS
        // LOOK LIKE KILLERPDF WHICH WE SPENT A LONG TIME PERFECTING".)
        //
        //   glyph    small 14   large 20   beside 16   under 20
        //   caption  beside 12  under 10
        //   button   icon-only  small 36x32, large 46x42, beside 40x34, under 46x52
        //   padding  icon-only 10,6   beside/under 8,5   text-only 8,5 with auto width
        //
        // The one deviation is deliberate: KillerPDF derives the GLYPH SIZE from the text mode
        // (under forces 20, beside forces 16), which is the very coupling Steve asked to break.
        // Here the size axis wins and the text mode only decides layout - so "small icons with
        // text under" is a real combination rather than being silently promoted to large.

        private const double GlyphSmall = 14;
        private const double GlyphLarge = 20;

        /// <summary>Applies KillerPDF's button box for the current combination.</summary>
        private static void SizeToolbarButton(Button btn, IconSize size, LabelMode mode)
        {
            bool large = size == IconSize.Large;
            switch (mode)
            {
                case LabelMode.Only:
                    btn.Width = double.NaN; btn.MinWidth = 0; btn.Height = 34;
                    btn.Padding = new Thickness(8, 5, 8, 5);
                    break;
                case LabelMode.Beside:
                    btn.Width = double.NaN; btn.MinWidth = 0; btn.Height = 34;
                    btn.Padding = new Thickness(8, 5, 8, 5);
                    break;
                case LabelMode.Under:
                    btn.Width = double.NaN; btn.MinWidth = 0; btn.Height = large ? 56 : 52;
                    btn.Padding = new Thickness(6, 4, 6, 4);
                    break;
                default:   // None - icon only, the fixed KillerPDF box
                    btn.Width = large ? 46 : 36;
                    btn.MinWidth = 0;
                    btn.Height = large ? 42 : 32;
                    btn.Padding = new Thickness(10, 6, 10, 6);
                    break;
            }
        }

        /// <summary>
        /// The button's content. Nothing here sets Foreground - see the class comment: the
        /// active-view highlight works by setting Foreground on the button and relying on
        /// inheritance to reach these.
        ///
        /// Size and text are independent, so an icon is sized by <paramref name="size"/> alone.
        /// The old code derived the icon size from the TEXT mode, which is exactly why the two
        /// could not be chosen separately.
        /// </summary>
        private static object BuildToolbarContent(IconSize size, LabelMode mode, int glyph, string label)
        {
            double glyphSize = size == IconSize.Large ? GlyphLarge : GlyphSmall;

            if (mode == LabelMode.Only)
                return new TextBlock
                {
                    Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                };

            var icon = new TextBlock
            {
                Text = ((char)glyph).ToString(),
                FontFamily = MdlFont,
                FontSize = glyphSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (mode == LabelMode.None) return icon;

            var stack = new StackPanel
            {
                Orientation = mode == LabelMode.Beside ? Orientation.Horizontal : Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var text = new TextBlock
            {
                Text = label,
                // KillerPDF: 12 beside, 10 under.
                FontSize = mode == LabelMode.Beside ? 12 : 10,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            if (mode == LabelMode.Beside) icon.Margin = new Thickness(0, 0, 7, 0);
            else                          text.Margin = new Thickness(0, 2, 0, 0);

            stack.Children.Add(icon);
            stack.Children.Add(text);
            return stack;
        }

        // ---- the right-click menu ----

        /// <summary>
        /// TWO radio groups, separated: icon size, then where the text goes. One flat list of five
        /// could not express "large icons WITH text", which is the whole reason this was split.
        /// </summary>
        private void BuildToolbarMenu()
        {
            ToolbarMenu.Items.Clear();
            ToolbarMenu.Items.Add(new MenuItem { Header = Loc("Str_Toolbar_Header"), IsEnabled = false });
            ToolbarMenu.Items.Add(new Separator());

            foreach (var (size, key) in IconSizes)
            {
                var mi = new MenuItem { Header = Loc(key), Tag = size, IsCheckable = true, IsChecked = size == _iconSize };
                // Checking one unchecks the rest through ApplyToolbarStyle, so this behaves as a
                // radio group without needing RadioButton plumbing inside a menu.
                var s = size;
                mi.Click += (_, __) => SetIconSize(s);
                ToolbarMenu.Items.Add(mi);
            }

            ToolbarMenu.Items.Add(new Separator());

            foreach (var (mode, key) in LabelModes)
            {
                var mi = new MenuItem { Header = Loc(key), Tag = mode, IsCheckable = true, IsChecked = mode == _labelMode };
                var m = mode;
                mi.Click += (_, __) => SetLabelMode(m);
                ToolbarMenu.Items.Add(mi);
            }
        }

        /// <summary>Rebuild captions and the menu after a live language change.</summary>
        private void RelocalizeToolbar()
        {
            BuildToolbarMenu();
            // ApplyToolbarStyle already rebuilds the overflow menu and invalidates the cached
            // widths, so there is nothing to repeat here. Calling them a second time only queued
            // redundant layout work.
            ApplyToolbarStyle();
        }
    }
}
