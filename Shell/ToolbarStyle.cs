using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KillerFamily.Accessibility;

// ============================================================
// TOOLBAR APPEARANCE - two independent settings
//
// The family standard. Numbers are KillerPDF's, from SettingsPanel.cs SetToolbarButton, which is
// where they were actually settled; the two-axis split is a later correction to them.
//
// COPY THIS FILE INTO THE APP, change the namespace, and supply:
//   * ToolbarItems()   - (Button, glyph codepoint, label key) for every button on the bar
//   * Loc(string)      - label lookup
//   * Settings.Get/Set - persistence
//   * a ContextMenu named ToolbarMenu on the toolbar Grid
//
// ------------------------------------------------------------
// TWO AXES, NOT ONE. KillerPDF has a single five-way enum:
//
//     SmallIcons | LargeIcons | TextBeside | TextUnder | TextOnly
//
// which cannot express "large icons WITH text", because choosing any text option throws the size
// choice away. They were never one axis, they only looked like one. (2026-07-30)
//
//     icon size:  Small | Large
//     text:       None | Beside | Under | Only
//
// Every combination is legal. "Only" ignores the size because there is no icon to size - the
// picker GRAYS the size options rather than hiding them, so the setting stays visible and comes
// back when text moves off Only.
//
// Consequence worth knowing: KillerPDF derives the glyph size from the text mode (under forces 20,
// beside forces 16). Do NOT carry that over - it is the coupling being removed. Here the size axis
// wins and the text mode only decides layout.
//
// ------------------------------------------------------------
// THE NUMBERS. These are KillerPDF's and they are not to be retuned per app - an app that picks
// its own comes out visibly smaller or tighter than the rest of the family, which is exactly what
// happened to Killendar (14/20 glyphs, 9,4 padding, no button box at all).
//
//   glyph     small 14, large 20
//   caption   beside 12, under 10
//   button    icon-only:  small 36x32, large 46x42
//             beside:     auto width, height 34, padding 8,5
//             under:      auto width, height 52 (56 large), padding 6,4
//             text-only:  auto width, height 34, padding 8,5
//   padding   icon-only 10,6
//
// GROUP SEPARATORS are a 1px Rectangle, not a wider margin:
//   <Rectangle Width="1" Fill="{DynamicResource CardBorderBrush}" Margin="4,6"/>
// KillerPDF names the brush BorderDim; CardBorderBrush is the same role in the kit palette.
//
// ------------------------------------------------------------
// TWO THINGS THAT WILL BREAK THIS IF CHANGED CARELESSLY
//   * If the app highlights an active button by setting the BUTTON's Foreground, nothing built
//     here may set Foreground on its own TextBlocks, or the highlight stops reaching them and
//     every active button looks inactive.
//   * If buttons carry state in Tag (Killendar's view buttons hold their view name), never write
//     to Tag from here.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private enum IconSize { Small, Large }
        private enum LabelMode { None, Beside, Under, Only }

        // Large icons with the text underneath is the family default (2026-07-30). Note it
        // is a combination the old five-way enum could not express: it had a TextUnder mode, but no
        // way to say whether the icon above the caption was large or small.
        //
        // The button name stays reachable in every combination - each one keeps its tooltip - which
        // is what makes the icon-only settings usable at all.
        private IconSize _iconSize = IconSize.Large;
        private LabelMode _labelMode = LabelMode.Under;

        private static readonly (IconSize Size, string LabelKey)[] IconSizes =
        {
            (IconSize.Small, "Str_Toolbar_SmallIcons"),
            (IconSize.Large, "Str_Toolbar_LargeIcons"),
        };

        private static readonly (LabelMode Mode, string LabelKey)[] LabelModes =
        {
            (LabelMode.None,   "Str_Toolbar_TextNone"),
            (LabelMode.Beside, "Str_Toolbar_TextBeside"),
            (LabelMode.Under,  "Str_Toolbar_TextUnder"),
            (LabelMode.Only,   "Str_Toolbar_TextOnly"),
        };

        private static readonly FontFamily MdlFont = new FontFamily("Segoe MDL2 Assets");

        private const double GlyphSmall = 14;
        private const double GlyphLarge = 20;

        // ---- apply ----

        /// <summary>
        /// Restores the saved settings. Call once at startup, AFTER the locale is in place - the
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
        /// Reads the retired single "ToolbarStyle" setting and splits it across the two new keys,
        /// so an existing install keeps the bar it was left on instead of silently resetting.
        /// Only runs when the new keys are absent. Drop this once no shipped build writes the old
        /// key any more.
        /// </summary>
        private void MigrateOldToolbarStyle()
        {
            switch (Settings.Get("ToolbarStyle"))
            {
                case "SmallIcons": _iconSize = IconSize.Small; _labelMode = LabelMode.None; break;
                case "LargeIcons": _iconSize = IconSize.Large; _labelMode = LabelMode.None; break;
                // The old text modes never said what size the icon was, so they take the default.
                case "TextBeside": _labelMode = LabelMode.Beside; break;
                case "TextUnder":  _labelMode = LabelMode.Under;  break;
                case "TextOnly":   _labelMode = LabelMode.Only;   break;
            }
        }

        private void SetIconSize(IconSize size)
        {
            _iconSize = size;
            Settings.Set("ToolbarIconSize", size.ToString());
            ApplyToolbarStyle();
        }

        private void SetLabelMode(LabelMode mode)
        {
            _labelMode = mode;
            Settings.Set("ToolbarLabels", mode.ToString());
            ApplyToolbarStyle();
        }

        private void ApplyToolbarStyle()
        {
            foreach (var (btn, glyph, key) in ToolbarItems())
            {
                var label = Loc(key);
                btn.Content = BuildToolbarContent(_iconSize, _labelMode, glyph, label);
                SizeToolbarButton(btn, _iconSize, _labelMode);
                object tip = label;
                ToolbarTooltip(btn, label, ref tip);
                btn.ToolTip = tip;
            }

            foreach (var item in ToolbarMenu.Items)
            {
                if (item is not MenuItem mi) continue;
                if (mi.Tag is IconSize szTag)
                {
                    mi.IsChecked = szTag == _iconSize;
                    // Text-only has no icon to size. Gray the choice rather than hiding it.
                    mi.IsEnabled = _labelMode != LabelMode.Only;
                }
                else if (mi.Tag is LabelMode lbTag) mi.IsChecked = lbTag == _labelMode;
            }

            // If the app has width-based toolbar overflow, it invalidates its cached widths in
            // OnToolbarStyleApplied - either change alters every button's width, so re-testing
            // the old numbers is wrong.
            OnToolbarStyleApplied();
        }

        /// <summary>App hook: replace a toolbar button's tooltip (the label by default), for
        /// example to add the button's shortcut.</summary>
        partial void ToolbarTooltip(Button btn, string label, ref object tip);

        /// <summary>App hook: runs after every restyle, for button sizes tied to other controls,
        /// overflow menus and cached widths.</summary>
        partial void OnToolbarStyleApplied();

        /// <summary>KillerPDF's button box for the current combination.</summary>
        private static void SizeToolbarButton(Button btn, IconSize size, LabelMode mode)
        {
            bool large = size == IconSize.Large;
            switch (mode)
            {
                case LabelMode.Only:
                case LabelMode.Beside:
                    btn.Width = double.NaN; btn.MinWidth = 0; btn.Height = 34;
                    btn.Padding = new Thickness(8, 5, 8, 5);
                    break;
                case LabelMode.Under:
                    btn.Width = double.NaN; btn.MinWidth = 0; btn.Height = large ? 50 : 46;
                    btn.Padding = new Thickness(6, 4, 6, 4);
                    break;
                default:   // None - icon only, the fixed box
                    btn.Width = large ? 46 : 36;
                    btn.MinWidth = 0;
                    btn.Height = large ? 42 : 32;
                    btn.Padding = new Thickness(10, 6, 10, 6);
                    break;
            }
        }

        /// <summary>
        /// The button's content. An icon is sized by <paramref name="size"/> ALONE - see the header
        /// on why the text mode must not influence it.
        /// </summary>
        private static object BuildToolbarContent(IconSize size, LabelMode mode, int glyph, string label)
        {
            double glyphSize = size == IconSize.Large ? GlyphLarge : GlyphSmall;

            if (mode == LabelMode.Only)
                return new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

            var icon = new Glyph
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

        // ---- the right-click picker ----

        /// <summary>
        /// TWO radio groups, separated: icon size, then where the text goes. One flat list of five
        /// cannot express "large icons WITH text", which is the whole reason this was split.
        ///
        /// Where the picker lives: right-click the toolbar. KillerPDF puts it in a settings panel;
        /// an app without one uses the context menu, which is the Windows convention anyway. The
        /// toolbar Grid needs Background="Transparent" - not null - or it gets no mouse events and
        /// the menu never opens.
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
            ApplyToolbarStyle();
        }
    }
}
