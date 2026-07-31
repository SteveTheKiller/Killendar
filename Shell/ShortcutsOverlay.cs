using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;   // the keycap hover lift
using System.Windows.Shapes;            // the accent bar under each bound key
using Killendar.Services;

// ============================================================
// The keyboard shortcuts overlay: F1, with a LIST view and a MAP view the user toggles between.
//
// ONE binding table drives both views and nothing else restates it. KillerPDF - where this
// pattern comes from - keeps its table twice, once in ShortcutsOverlay.cs for the list and again
// in KeyboardMapOverlay.cs for the map, and the two can drift. Killendar has ~20 bindings, so a
// single table is easy and the drift is designed out instead of managed.
//
// This is a MainWindow partial in Shell/ rather than a Features/ controller on purpose: it reads
// no store, owns no state worth testing headlessly, and every method here needs the window's own
// named elements. It sits beside Shortcuts.cs, which owns the key handling it documents.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        /// <summary>What a binding belongs to. Drives the colour of the bar under a key on the
        /// map and the group it lands in on the list. Same five the website map uses.</summary>
        private enum KsCat { View, Nav, Appt, File, Help }

        /// <summary>
        /// One binding. <paramref name="Caps"/> are the map key ids it should light (the ids in
        /// KbRows), so a single action can own several keys - M and 1 are both Month view.
        /// LabelKey is a Str_* resource key so the overlay follows the active locale.
        /// </summary>
        private readonly struct KsBinding(string keys, string labelKey, MainWindow.KsCat cat, bool ctrl, params string[] caps)
        {
            public readonly string Keys = keys;        // how it reads on the list, e.g. "M or 1"
            public readonly string LabelKey = labelKey;    // Str_* resource key
            public readonly KsCat Cat = cat;
            public readonly bool Ctrl = ctrl;        // lives on the Ctrl layer of the map
            public readonly string[] Caps = caps;      // map key ids to light
        }

        // ---- THE table. Mirrors Shell/Shortcuts.cs exactly; if a key changes there, change it
        //      here, and in killendar-landing/technical.html which hard-codes the same set. ----
        private static readonly KsBinding[] KsAll =
        [
            new("M or 1",   "Str_KS_Month",    KsCat.View, false, "M", "D1"),
            new("W or 2",   "Str_KS_Week",     KsCat.View, false, "W", "D2"),
            new("D or 3",   "Str_KS_Day",      KsCat.View, false, "D", "D3"),
            new("A or 4",   "Str_KS_Agenda",   KsCat.View, false, "A", "D4"),

            new("Left / ,", "Str_KS_Prev",     KsCat.Nav,  false, "Left", "Comma"),
            new("Right / .","Str_KS_Next",     KsCat.Nav,  false, "Right", "Period"),
            new("T",        "Str_KS_Today",    KsCat.Nav,  false, "T"),

            new("N",        "Str_KS_New",      KsCat.Appt, false, "N"),
            new("B",        "Str_KS_Panel",    KsCat.Appt, false, "B"),
            new("Ctrl+N",   "Str_KS_New",      KsCat.Appt, true,  "N"),

            new("Ctrl+I",   "Str_KS_Import",   KsCat.File, true,  "I"),
            new("Ctrl+E",   "Str_KS_Export",   KsCat.File, true,  "E"),

            new("F1",       "Str_KS_ThisList", KsCat.Help, false, "F1"),
            new("F12",      "Str_KS_About",    KsCat.Help, false, "F12"),
            new("Esc",      "Str_KS_Close",    KsCat.Help, false, "Esc"),
        ];

        // Category colours are the KsCat* THEME BRUSHES in Themes/*.xaml - "KsCat" + the enum name.
        // They are not listed here any more: a hardcoded table cannot be retuned per theme, and the
        // neon set that is right on Dark is close to invisible on Light. Same mechanism, same
        // values and same per-theme retune as KillerPDF and KillerShell. (Steve, 2026-07-30.)

        private static readonly (KsCat Cat, string TitleKey)[] KsGroups =
        [
            (KsCat.View, "Str_KS_GrpViews"), (KsCat.Nav,  "Str_KS_GrpNav"),
            (KsCat.Appt, "Str_KS_GrpAppt"),  (KsCat.File, "Str_KS_GrpFile"),
            (KsCat.Help, "Str_KS_GrpHelp"),
        ];

        // Arrow and hamburger key caps, built from codepoints rather than typed literally. This
        // file is BOM-less UTF-8 and must stay 0 non-ASCII bytes - the same rule About.cs and the
        // rail chevrons follow, for the same reason: a literal glyph does not survive tooling.
        // These MUST be declared before KbRows, because static field initializers run in
        // declaration order and KbRows reads them.
        private static readonly string CapLeft  = ((char)0x2190).ToString();   // left arrow
        private static readonly string CapRight = ((char)0x2192).ToString();   // right arrow
        private static readonly string CapDown  = ((char)0x2193).ToString();   // down arrow
        private static readonly string CapMenu  = ((char)0x2630).ToString();   // trigram, the menu key

        // Keyboard rows for the map: (id, cap, width units). An empty id is a gap.
        private static readonly (string Id, string Cap, double W)[][] KbRows =
        [
            [("Esc","Esc",1), ("","",0.6), ("F1","F1",1), ("F2","F2",1), ("F3","F3",1), ("F4","F4",1), ("","",0.4),
             ("F5","F5",1), ("F6","F6",1), ("F7","F7",1), ("F8","F8",1), ("","",0.4),
             ("F9","F9",1), ("F10","F10",1), ("F11","F11",1), ("F12","F12",1)],
            [("Grave","`",1), ("D1","1",1), ("D2","2",1), ("D3","3",1), ("D4","4",1), ("D5","5",1), ("D6","6",1),
             ("D7","7",1), ("D8","8",1), ("D9","9",1), ("D0","0",1), ("Minus","-",1), ("Equals","=",1), ("Back",CapLeft,2)],
            [("Tab","Tab",1.5), ("Q","Q",1), ("W","W",1), ("E","E",1), ("R","R",1), ("T","T",1), ("Y","Y",1),
             ("U","U",1), ("I","I",1), ("O","O",1), ("P","P",1), ("LBr","[",1), ("RBr","]",1), ("BSl","\\",1.5)],
            [("Caps","Caps",1.8), ("A","A",1), ("S","S",1), ("D","D",1), ("F","F",1), ("G","G",1), ("H","H",1),
             ("J","J",1), ("K","K",1), ("L","L",1), ("Semi",";",1), ("Quote","'",1), ("Enter","Enter",2.2)],
            [("Shift","Shift",2.3), ("Z","Z",1), ("X","X",1), ("C","C",1), ("V","V",1), ("B","B",1), ("N","N",1),
             ("M","M",1), ("Comma",",",1), ("Period",".",1), ("Slash","/",1), ("RShift","Shift",2.7)],
            [("Ctrl","Ctrl",1.5), ("Win","Win",1.2), ("Alt","Alt",1.5), ("Space","",6.8), ("RAlt","Alt",1.5),
             ("Menu",CapMenu,1), ("RCtrl","Ctrl",1.5), ("","",0.4),
             ("Left",CapLeft,1), ("Down",CapDown,1), ("Right",CapRight,1)],
        ];

        // 46, matching KillerPDF's U. Killendar was drawing at 42, so the same keyboard came out
        // ~9% smaller than the reference for no reason. (Steve, 2026-07-30.)
        private const double KbUnit = 46;
        private bool _ksMapView;

        // Loc() is NOT redeclared here: Shell/Language.cs already defines it on MainWindow, and a
        // second one with the same signature in another partial is CS0111.

        // ---- Show / hide ----

        private void ToggleShortcutsOverlay()
        {
            if (ShortcutsOverlay.Visibility == Visibility.Visible) { FadeOverlayOut(ShortcutsOverlay); return; }
            // Only one overlay at a time - About and this one would otherwise stack.
            if (AboutOverlay.Visibility == Visibility.Visible) AboutOverlay.Visibility = Visibility.Collapsed;
            ApplyShortcutView(read: true);
            ShortcutsOverlay.Visibility = Visibility.Visible;
            Controls.Anim.FadeIn(ShortcutsOverlay);
        }

        private void ShortcutsButton_Click(object sender, RoutedEventArgs e) => ToggleShortcutsOverlay();
        private void ShortcutsOverlay_Click(object sender, MouseButtonEventArgs e) => FadeOverlayOut(ShortcutsOverlay);
        private void ShortcutsCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
        private void ShortcutsClose_Click(object sender, RoutedEventArgs e) => FadeOverlayOut(ShortcutsOverlay);

        private void KsViewList_Click(object sender, RoutedEventArgs e)     => ApplyShortcutView(false, persist: true);
        private void KsViewKeyboard_Click(object sender, RoutedEventArgs e) => ApplyShortcutView(true,  persist: true);

        /// <summary>Switches the overlay between the list and the map. The choice persists, so the
        /// one you prefer is the one that opens next time.</summary>
        private void ApplyShortcutView(bool keyboard = false, bool persist = false, bool read = false)
        {
            if (read) keyboard = Settings.Get("ShortcutsMapView") == "1";
            _ksMapView = keyboard;
            if (persist) Settings.Set("ShortcutsMapView", keyboard ? "1" : "0");

            // Toggle the ScrollViewer, not the StackPanel inside it: collapsing the inner panel
            // leaves the scroller in the tree still holding its MaxHeight.
            KsListHostScroll.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
            KsMapHost.Visibility        = keyboard ? Visibility.Visible   : Visibility.Collapsed;
            KsViewList.Tag     = keyboard ? null : "on";
            KsViewKeyboard.Tag = keyboard ? "on" : null;

            // The card is sized per VIEW, as KillerPDF does it (ApplyShortcutView: 1080 keyboard,
            // 640 list). A keyboard needs the room; a list of shortcut rows at 1080 is a wall of
            // whitespace with the keys on one edge and the descriptions on the other.
            ShortcutCardGrid.MaxWidth = keyboard ? 1080 : 640;

            if (keyboard) BuildKeyboardMap(); else BuildShortcutsList();
        }

        // ---- LIST view ----

        private void BuildShortcutsList()
        {
            KsListHost.Children.Clear();
            bool first = true;
            foreach (var (cat, titleKey) in KsGroups)
            {
                var header = new TextBlock
                {
                    Text = Loc(titleKey),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, first ? 0 : 14, 0, 5),
                };
                // Theme brush, like the map - the LIST's section headings are the same category
                // colours, which is what ties the two views together.
                header.SetResourceReference(TextBlock.ForegroundProperty, "KsCat" + cat);
                KsListHost.Children.Add(header);
                first = false;

                foreach (var b in KsAll.Where(x => x.Cat == cat))
                {
                    var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var keys = new TextBlock
                    {
                        Text = b.Keys, FontFamily = new FontFamily("Consolas"),
                        FontSize = 11.5, Width = 96,
                    };
                    keys.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                    dock.Children.Add(keys);

                    var label = new TextBlock { Text = Loc(b.LabelKey), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
                    label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    dock.Children.Add(label);

                    KsListHost.Children.Add(dock);
                }
            }
        }

        // ---- MAP view ----

        private void BuildKeyboardMap()
        {
            KsMapRows.Children.Clear();
            // Which cap ids are lit, and with what, on the layer being shown.
            var lit = new Dictionary<string, KsBinding>();
            foreach (var b in KsAll.Where(x => x.Ctrl == _ksCtrlLayer))
                foreach (var cap in b.Caps)
                    lit[cap] = b;

            foreach (var row in KbRows)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                foreach (var (id, cap, w) in row)
                {
                    if (id.Length == 0)
                    {
                        panel.Children.Add(new Border { Width = KbUnit * w });
                        continue;
                    }
                    panel.Children.Add(BuildKeyCap(cap, w, lit.TryGetValue(id, out var b) ? b : (KsBinding?)null));
                }
                KsMapRows.Children.Add(panel);
            }

            // The detail line under the board - KillerPDF's _kbDetail. The hovered key's shortcut
            // and full action, at a size you can actually read, so a caption that had to be
            // ellipsised on a narrow cap is still recoverable.
            _kbDetail = new TextBlock
            {
                Text = " ", FontFamily = new FontFamily("Consolas"), FontSize = 12.5,
                Margin = new Thickness(2, 10, 0, 0), Height = 18,
            };
            _kbDetail.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            KsMapRows.Children.Add(_kbDetail);

            KsLayerBase.Tag = _ksCtrlLayer ? null : "on";
            KsLayerCtrl.Tag = _ksCtrlLayer ? "on" : null;
        }

        /// <summary>Reads out the hovered key. Rebuilt with the board, so it is re-created on every
        /// layer switch rather than held across one.</summary>
        private TextBlock? _kbDetail;

        private bool _ksCtrlLayer;

        private void KsLayerBase_Click(object sender, RoutedEventArgs e) { _ksCtrlLayer = false; BuildKeyboardMap(); }
        private void KsLayerCtrl_Click(object sender, RoutedEventArgs e) { _ksCtrlLayer = true;  BuildKeyboardMap(); }

        /// <summary>
        /// One keycap, built to KillerPDF's geometry exactly - KeyboardMapOverlay.cs BuildKeyboardView
        /// is the source of truth for this map and Killendar had drifted from it:
        ///
        ///   * a Grid, not a StackPanel. The cap letter is pinned TOP and the action caption pinned
        ///     BOTTOM, so both sit at the same height on every key whatever the caption's length.
        ///     Stacked, they centred as a group and the letters wandered up and down the row.
        ///   * the action caption lives in a ClipToBounds Border so a long one can marquee on hover
        ///     instead of being silently ellipsised - that caption is the whole point of the map.
        ///   * hover lifts the cap 3px, the same lift the killertools.net cards use.
        ///
        /// The one deliberate difference is colour: KillerPDF has a single Accent, Killendar tints
        /// each key by its category (the neon set shared with the website map).
        /// </summary>
        private Border BuildKeyCap(string cap, double w, KsBinding? bound)
        {
            var capText = new TextBlock
            {
                Text = cap, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0),
            };
            capText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var act = new TextBlock
            {
                FontSize = 8.5, HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed,
                RenderTransform = new TranslateTransform(),
            };
            var actHost = new Border
            {
                ClipToBounds = true, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 2, 5), Child = act,
            };
            var bar = new Rectangle
            {
                Height = 3, VerticalAlignment = VerticalAlignment.Bottom, RadiusX = 1.5, RadiusY = 1.5,
                Margin = new Thickness(3, 0, 3, 0), Visibility = Visibility.Collapsed,
            };

            var inner = new Grid();
            inner.Children.Add(capText);
            inner.Children.Add(actHost);
            inner.Children.Add(bar);

            var key = new Border
            {
                Width = KbUnit * w - 4, Height = 44, CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0),
                Child = inner,
            };
            key.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");

            if (bound is KsBinding b)
            {
                // THEME BRUSHES, via SetResourceReference - "KsCat" + the category name, exactly as
                // KillerPDF and KillerShell do it. These used to be hardcoded hex in KsColors,
                // which meant the neon set was painted on the Light theme too, where it is close to
                // invisible, and a live theme switch left the board on the old palette.
                // (Steve, 2026-07-30.)
                string catKey = "KsCat" + b.Cat;
                key.SetResourceReference(Border.BorderBrushProperty, catKey);
                act.Text = Loc(b.LabelKey);
                act.SetResourceReference(TextBlock.ForegroundProperty, catKey);
                act.Visibility = Visibility.Visible;
                bar.SetResourceReference(Shape.FillProperty, catKey);
                bar.Visibility = Visibility.Visible;

                // Only a bound key lifts - dummies staying put is what makes the lit ones read as
                // the interactive set.
                var lift = new TranslateTransform();
                key.RenderTransform = lift;
                key.MouseEnter += (_, _2) =>
                {
                    if (_kbDetail != null) _kbDetail.Text = b.Keys + "   " + Loc(b.LabelKey);
                    lift.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-3, TimeSpan.FromMilliseconds(90)));
                };
                key.MouseLeave += (_, _2) =>
                {
                    if (_kbDetail != null) _kbDetail.Text = " ";
                    lift.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)));
                };
            }
            else
            {
                key.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                capText.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
            }
            return key;
        }

    }
}
