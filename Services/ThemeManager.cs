using System;
using System.Windows;
using System.Windows.Media;


namespace Killendar.Services
{
    public enum Theme
    {
        Dark, Light, Black, SE98, Blood, Greed, Cyanotic, Ectoplasm, Decay,
        Mourning, Sepulchre, Delirium, Malaise
    }

    // Accent-hue variants for the accent-capable families (Dark, Light, Black).
    // Green is the base theme (no overlay); the others apply a small overlay
    // dictionary that recolors only the accent-family keys.
    public enum Accent { Green, Red, Blue, Purple, Orange, Teal }

    /// <summary>
    /// Swaps the theme color dictionary (MergedDictionaries[0]) in place at runtime.
    /// Control styles live in Controls.xaml and bind brushes via DynamicResource, so an
    /// in-place per-key update repaints everything without structural churn.
    ///
    /// Persistence is decoupled: wire GetSetting/SetSetting to your storage (registry,
    /// JSON, etc.) at startup if you want the choice to survive restarts. Left unset,
    /// the theme still works for the session, it just won't be remembered.
    ///
    /// REQUIRES (as app resources, merged in App.xaml before Controls.xaml):
    ///   MergedDictionaries[0] = a Themes/{Theme}.xaml color dictionary.

    /// </summary>
    public static class ThemeManager
    {
        // ---- Persistence hooks (optional). Default: in-memory only. ----
        public static Func<string, string?> GetSetting { get; set; } = _ => null;
        public static Action<string, string> SetSetting { get; set; } = (_, _) => { };

        // Default theme/accent when nothing is stored. Tweak per app if you like.
        //
        // Killendar ships DARK + RED. The three neutral families each define
        // their own red - Dark #DD504B, Light #931A1A, Black #FF2929 - and the brand wordmark, the
        // og-image and killendar.net are all built on Dark's #DD504B. Defaulting to Black + Red
        // would open the app in #FF2929 and not match its own branding. Black + Red is still one
        // click away in the theme flyout; only the default moved.
        private static Theme _current = Theme.Dark;
        private static Accent _darkAccent  = Accent.Red;
        private static Accent _lightAccent = Accent.Green;
        private static Accent _blackAccent = Accent.Red;
        private static Accent _se98Accent  = Accent.Blue;

        public static Theme Current => _current;
        public static Accent AccentChoiceFor(Theme t) => AccentFor(t);

        private static Accent AccentFor(Theme t) =>
            t == Theme.Light ? _lightAccent
                : t == Theme.Black ? _blackAccent
                : t == Theme.SE98 ? _se98Accent
                : _darkAccent;

        // Only these families carry accent-hue overlays.
        private static bool HasAccents(Theme t) =>
            t == Theme.Dark || t == Theme.Light || t == Theme.Black || t == Theme.SE98;

        /// <summary>Fired after the theme dictionary has been updated.</summary>
        public static event Action? ThemeChanged;

        /// <summary>Call once at startup, before the main window is created, to restore the saved theme.</summary>
        public static void Initialize()
        {
            _current     = Enum.TryParse<Theme>(GetSetting("Theme"),        out var t)  ? t  : _current;
            _darkAccent  = Enum.TryParse<Accent>(GetSetting("DarkAccent"),  out var da) ? da : _darkAccent;
            _lightAccent = Enum.TryParse<Accent>(GetSetting("LightAccent"), out var la) ? la : _lightAccent;
            _blackAccent = Enum.TryParse<Accent>(GetSetting("BlackAccent"), out var ba) ? ba : _blackAccent;
            _se98Accent  = Enum.TryParse<Accent>(GetSetting("98SEAccent"),  out var wa) ? wa : _se98Accent;
            LoadDict(_current);
        }

        /// <summary>Change theme, persist the choice, and repaint.</summary>
        public static void Apply(Theme theme)
        {
            _current = theme;
            SetSetting("Theme", theme.ToString());
            LoadDict(theme);
            ThemeChanged?.Invoke();
        }

        /// <summary>
        /// Change a family's accent hue, persist it, and reapply if that family is active.
        /// Dark/Light/Black keep independent accents, so changing one never disturbs another.
        /// </summary>
        public static void ApplyAccent(Theme family, Accent accent)
        {
            if      (family == Theme.Light) { _lightAccent = accent; SetSetting("LightAccent", accent.ToString()); }
            else if (family == Theme.Black) { _blackAccent = accent; SetSetting("BlackAccent", accent.ToString()); }
            else if (family == Theme.SE98)  { _se98Accent  = accent; SetSetting("98SEAccent",  accent.ToString()); }
            else                            { _darkAccent  = accent; SetSetting("DarkAccent",  accent.ToString()); }

            if (_current == family)
            {
                LoadDict(_current);
                ThemeChanged?.Invoke();
            }
        }

        private static void LoadDict(Theme theme)
        {
            var uri = theme == Theme.SE98
                ? new Uri("pack://application:,,,/Themes/98SE.xaml")
                : new Uri($"pack://application:,,,/Themes/{theme}.xaml");
            var newDict = new ResourceDictionary { Source = uri };
            CompleteKillendarPalette(newDict, theme);
            var merged  = Application.Current.Resources.MergedDictionaries;

            // In-place per-key update: fires a targeted change notification for each key without
            // structurally modifying MergedDictionaries (a structural swap fires a synchronous
            // ResourcesChanged that can re-enter lookups before the new dict is fully in place).
            if (merged.Count > 0)
            {
                var existing = merged[0];
                foreach (object key in newDict.Keys)
                    existing[key] = newDict[key];
            }
            else
            {
                merged.Add(newDict);
            }

            // Accent overlay: Dark/Light/Black recolor their accent-family keys on top of the base
            // green. Green is the base itself, so it needs no overlay. Overlays live in Accents/<Family>/.
            var accent = AccentFor(theme);
            if (HasAccents(theme) && accent != Accent.Green)
            {
                string family = theme == Theme.Light ? "Light"
                    : theme == Theme.Black ? "Black"
                    : theme == Theme.SE98 ? "98SE"
                    : "Dark";
                try
                {
                    var accentDict = new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/Themes/Accents/{family}/{accent}.xaml")
                    };
                    var target = merged[0];
                    foreach (object key in accentDict.Keys)
                        target[key] = accentDict[key];
                }
                catch { /* overlay file not present - base theme stands */ }
            }
            // The classic calendar is a white client area. Give unclassified appointments a
            // pale version of the selected 98SE accent instead of the old face gray, which could
            // disappear into the calendar. Each accent therefore owns its own readable shade.
            if (theme == Theme.SE98 && merged[0]["PrimaryBrush"] is SolidColorBrush primary)
            {
                Color p = primary.Color;
                merged[0]["ChipBrush"] = new SolidColorBrush(Color.FromRgb(
                    (byte)(255 * 0.84 + p.R * 0.16),
                    (byte)(255 * 0.84 + p.G * 0.16),
                    (byte)(255 * 0.84 + p.B * 0.16)));
            }
            // Calendar structure is intentionally only TWO tones: every weekday shares the base
            // pane, and Saturday/Sunday receive one dark wash. Earlier versions alternated accent
            // strengths by display column, which arbitrarily made Tuesday and Thursday dark and
            // produced five apparent shades once selection/today were added.
            SolidColorBrush? calendarAccent = theme == Theme.Ectoplasm
                ? merged[0]["InputBorderBrush"] as SolidColorBrush
                : merged[0]["PrimaryBrush"] as SolidColorBrush;
            if (calendarAccent != null)
            {
                byte weekend = theme == Theme.SE98 ? (byte)24 : (byte)56;
                merged[0]["CalendarEvenColumnBrush"] = Brushes.Transparent;
                merged[0]["CalendarOddColumnBrush"] = Brushes.Transparent;
                merged[0]["CalendarWeekendBrush"] = new SolidColorBrush(Color.FromArgb(weekend, 0, 0, 0));
                // Spillover dates are a different state, not another accent stripe. Give them an
                // opaque dark client color so none of the weekday tint can bleed through.
                Color outsideBase = merged[0]["PaneBrush"] is SolidColorBrush pane
                    ? pane.Color : Color.FromRgb(42, 42, 42);
                double outsideScale = theme == Theme.SE98 ? 0.48 : 0.38;
                merged[0]["CalendarOutsideMonthBrush"] = new SolidColorBrush(Color.FromRgb(
                    (byte)(outsideBase.R * outsideScale),
                    (byte)(outsideBase.G * outsideScale),
                    (byte)(outsideBase.B * outsideScale)));
                merged[0]["CalendarEvenHeaderBrush"] = Brushes.Transparent;
                merged[0]["CalendarOddHeaderBrush"] = Brushes.Transparent;
                merged[0]["CalendarWeekendHeaderBrush"] = new SolidColorBrush(
                    Color.FromArgb(theme == Theme.SE98 ? (byte)16 : (byte)36, 0, 0, 0));

            }
            // Aliases created from the base palette hold the old brush object. Refresh the ones
            // whose sources may have been replaced by an accent overlay.
            merged[0]["OutlineRestBrush"] = merged[0]["OutlineBtnBrush"];
            merged[0]["KsCatAppt"] = merged[0]["PrimaryBrush"];
            // About and Keyboard Shortcuts are window surfaces. Resolve this after accent merging
            // so the exact BackgroundBrush object (including gradients) is retained.
            merged[0]["OverlayWindowBrush"] = merged[0]["BackgroundBrush"];
        }

        /// <summary>
        /// Supplies Killendar-specific aliases plus the shared geometry contract to palettes that
        /// predate those keys. Modern themes are rounded; 98SE owns zero-valued radius tokens in
        /// its theme file and therefore keeps its intentional square chrome.
        /// </summary>
        private static void CompleteKillendarPalette(ResourceDictionary d, Theme theme)
        {
            void Alias(string key, string source)
            {
                if (!d.Contains(key) && d.Contains(source)) d[key] = d[source];
            }

            Alias("ChipBrush", "RowHoverBrush");
            Alias("SurfaceHoverBrush", "RowHoverBrush");
            Alias("KsCatAppt", "PrimaryBrush");
            Alias("AppBorderBrush", "CardBorderBrush");
            Alias("OutlineRestBrush", "OutlineBtnBrush");
            // The theme picker's checked row while hovered; Sepulchre sets it white in its
            // own file because its accent is its hover fill. KillerPDF's key.
            Alias("RadioHoverFgBrush", "PrimaryBrush");
            Alias("TitleBarBrush", "BackgroundBrush");
            Alias("ChromeTextBrush", "TextBrush");
            // Resolved after accent merging so gradient themes can use their left-edge chrome
            // color without stretching the whole window gradient across a narrow panel.
            Alias("DialogTitleBarBrush", "BackgroundBrush");
            Alias("InputFieldBrush", "SurfaceBrush");
            Alias("AboutPanelBrush", "PaneBrush");

            if (!d.Contains("BevelLightBrush")) d["BevelLightBrush"] = Brushes.Transparent;
            if (!d.Contains("BevelDarkBrush")) d["BevelDarkBrush"] = Brushes.Transparent;
            if (!d.Contains("ButtonBevelLightThickness")) d["ButtonBevelLightThickness"] = new Thickness(0);
            if (!d.Contains("ButtonBevelDarkThickness")) d["ButtonBevelDarkThickness"] = new Thickness(0);

            bool square = theme == Theme.SE98;
            d["TitleBarGridLength"] = new GridLength(square ? 22 : 36);
            d["ContentPaneMargin"] = square ? new Thickness(0) : new Thickness(0, 0, 8, 0);
            // Modern sidebars are transparent so the app gradient/grain continues through them.
            // 98SE uses a white client pane instead of exposing the gray window/button face.
            d["SidebarPaneBrush"] = square ? d["PaneBrush"] : Brushes.Transparent;
            // A hosted view can paint over a Border's normal border rendering. Keep the classic
            // border in layout for its bevel, but draw modern themes' single-pixel outline above
            // the view so it cannot disappear.
            d["ContentPaneBaseBorderThickness"] = square ? new Thickness(1) : new Thickness(0);
            d["ContentPaneOutlineThickness"] = square ? new Thickness(0) : new Thickness(1);
            // A shadow cast by the whole selected tile covers far more area than an icon shadow;
            // use a lighter opacity so the two read at the same visual weight.
            d["SelectedTileShadowOpacity"] = square ? 0d : 0.45d;
            d["GripDotsVisibility"] = square ? Visibility.Collapsed : Visibility.Visible;
            d["GripHatchVisibility"] = square ? Visibility.Visible : Visibility.Collapsed;
            d["SidebarHeadingFont"] = square
                ? new FontFamily("Courier New")
                : (Application.Current.TryFindResource("WordmarkFont") as FontFamily ?? new FontFamily("Consolas"));
            if (!d.Contains("WindowCornerRadius")) d["WindowCornerRadius"] = new CornerRadius(square ? 0 : 8);
            if (!d.Contains("PanelCornerRadius")) d["PanelCornerRadius"] = new CornerRadius(square ? 0 : 6);
            if (!d.Contains("FlyoutCornerRadius")) d["FlyoutCornerRadius"] = new CornerRadius(square ? 0 : 6);
            if (!d.Contains("ControlCornerRadius")) d["ControlCornerRadius"] = new CornerRadius(square ? 0 : 4);
            if (!d.Contains("SmallCornerRadius")) d["SmallCornerRadius"] = new CornerRadius(square ? 0 : 3);
            if (!d.Contains("AccentSwatchCornerRadius")) d["AccentSwatchCornerRadius"] = new CornerRadius(square ? 0 : 9);
            d["ScrollThumbRadius"] = square ? new CornerRadius(0) : new CornerRadius(6);
            d["DialogTitleCornerRadius"] = square ? new CornerRadius(0) : new CornerRadius(6, 6, 0, 0);
            d["CaptionCloseCornerRadius"] = square ? new CornerRadius(0) : new CornerRadius(0, 6, 0, 0);
            d["DialogTitleTextMargin"] = square ? new Thickness(4, 0, 0, 0) : new Thickness(14, 0, 0, 0);
            d["DialogCaptionCloseMargin"] = square ? new Thickness(0, 2, 3, 2) : new Thickness(0);

            // These keys are structural 98SE chrome. Themes are merged into one live dictionary,
            // so modern palettes must actively reset them or a switch away from 98SE leaves the
            // classic frame/title state behind.
            if (!d.Contains("WordmarkVisibility")) d["WordmarkVisibility"] = Visibility.Visible;
            if (!d.Contains("PlainTitleVisibility")) d["PlainTitleVisibility"] = Visibility.Collapsed;
            // Family title-bar contract used by KillerScan, KillerNotes and KillerShell. 98SE
            // supplies its own smaller values; every modern theme shares these defaults.
            if (!d.Contains("TitleBarPadding")) d["TitleBarPadding"] = new Thickness(14, 0, 0, 0);
            if (!d.Contains("TitleIconSize")) d["TitleIconSize"] = 22d;
            if (!d.Contains("TitleIconMargin")) d["TitleIconMargin"] = new Thickness(0, 0, 6, 0);
            if (!d.Contains("TitleWordmarkSize")) d["TitleWordmarkSize"] = 17d;
            if (!d.Contains("TitleWordmarkBoldSize")) d["TitleWordmarkBoldSize"] = 22.1d;
            if (!d.Contains("TitleTextMargin")) d["TitleTextMargin"] = new Thickness(0);
            if (!d.Contains("WordmarkEmbossOpacity")) d["WordmarkEmbossOpacity"] = 0d;
            if (!d.Contains("WindowFramePadding")) d["WindowFramePadding"] = new Thickness(0);
            if (!d.Contains("WindowFrameMargin")) d["WindowFrameMargin"] = new Thickness(0);
            if (!d.Contains("FrameInnerMargin")) d["FrameInnerMargin"] = new Thickness(0);
            if (!d.Contains("FrameOuterLightBrush")) d["FrameOuterLightBrush"] = Brushes.Transparent;
            if (!d.Contains("FrameOuterDarkBrush")) d["FrameOuterDarkBrush"] = Brushes.Transparent;
            if (!d.Contains("WindowFrameBrush")) d["WindowFrameBrush"] = Brushes.Transparent;
            if (!d.Contains("FrameInnerLightBrush")) d["FrameInnerLightBrush"] = Brushes.Transparent;
            if (!d.Contains("FrameInnerDarkBrush")) d["FrameInnerDarkBrush"] = Brushes.Transparent;
            if (!d.Contains("FrameOuterLightThickness")) d["FrameOuterLightThickness"] = new Thickness(0);
            if (!d.Contains("FrameOuterDarkThickness")) d["FrameOuterDarkThickness"] = new Thickness(0);
            if (!d.Contains("WindowFrameThickness")) d["WindowFrameThickness"] = new Thickness(0);
            if (!d.Contains("FrameInnerLightThickness")) d["FrameInnerLightThickness"] = new Thickness(0);
            if (!d.Contains("FrameInnerDarkThickness")) d["FrameInnerDarkThickness"] = new Thickness(0);
            if (!d.Contains("PaneShadowOpacity")) d["PaneShadowOpacity"] = square ? 0d : 0.6d;
            if (!d.Contains("PaneBevelDarkBrush")) d["PaneBevelDarkBrush"] = Brushes.Transparent;
            if (!d.Contains("PaneBevelLightBrush")) d["PaneBevelLightBrush"] = Brushes.Transparent;
            if (!d.Contains("PaneBevelDark2Brush")) d["PaneBevelDark2Brush"] = Brushes.Transparent;
            if (!d.Contains("PaneBevelLight2Brush")) d["PaneBevelLight2Brush"] = Brushes.Transparent;
            if (!d.Contains("PaneBevelLightThickness")) d["PaneBevelLightThickness"] = new Thickness(0);
            if (!d.Contains("PaneBevelDarkThickness")) d["PaneBevelDarkThickness"] = new Thickness(0);
            if (!d.Contains("PaneBevel2LightThickness")) d["PaneBevel2LightThickness"] = new Thickness(0);
            if (!d.Contains("PaneBevel2DarkThickness")) d["PaneBevel2DarkThickness"] = new Thickness(0);
            if (!d.Contains("PaneBevelInnerMargin")) d["PaneBevelInnerMargin"] = new Thickness(0);
            d["OverlayBevelOuterLightBrush"] = square ? d["FrameOuterLightBrush"] : Brushes.Transparent;
            d["OverlayBevelOuterDarkBrush"] = square ? d["FrameOuterDarkBrush"] : Brushes.Transparent;
            d["OverlayBevelInnerLightBrush"] = square ? d["BevelLightBrush"] : Brushes.Transparent;
            d["OverlayBevelInnerDarkBrush"] = square ? d["BevelDarkBrush"] : Brushes.Transparent;
            d["OverlayBevelLightThickness"] = square ? new Thickness(1, 1, 0, 0) : new Thickness(0);
            d["OverlayBevelDarkThickness"] = square ? new Thickness(0, 0, 1, 1) : new Thickness(0);
            d["OverlayBevelInnerMargin"] = square ? new Thickness(1) : new Thickness(0);
            if (!d.Contains("TitleBarHeight")) d["TitleBarHeight"] = 36d;
            if (!d.Contains("DialogTitleBarHeight")) d["DialogTitleBarHeight"] = 36d;
            if (!d.Contains("CaptionButtonWidth")) d["CaptionButtonWidth"] = 44d;
            if (!d.Contains("CaptionButtonHeight")) d["CaptionButtonHeight"] = 36d;
            if (!d.Contains("CaptionButtonMargin")) d["CaptionButtonMargin"] = new Thickness(0);
            if (!d.Contains("CaptionButtonsMargin")) d["CaptionButtonsMargin"] = new Thickness(0);
            if (!d.Contains("CaptionCloseGap")) d["CaptionCloseGap"] = new Thickness(0);
            if (!d.Contains("CaptionButtonBrush")) d["CaptionButtonBrush"] = Brushes.Transparent;
            if (!d.Contains("CaptionHoverBrush")) d["CaptionHoverBrush"] = d["RowHoverBrush"];
            if (!d.Contains("CaptionGlyphBrush")) d["CaptionGlyphBrush"] = d["TextBrush"];
            if (!d.Contains("CaptionCloseBrush")) d["CaptionCloseBrush"] = new SolidColorBrush(Color.FromRgb(224, 68, 68));
            if (!d.Contains("CaptionCloseHoverBrush")) d["CaptionCloseHoverBrush"] = new SolidColorBrush(Color.FromRgb(224, 68, 68));
            if (!d.Contains("CaptionCloseHoverFgBrush")) d["CaptionCloseHoverFgBrush"] = Brushes.White;
            if (!d.Contains("FlyoutShadowOpacity")) d["FlyoutShadowOpacity"] = 0.55d;
            if (!d.Contains("MenuBevelLightBrush")) d["MenuBevelLightBrush"] = Brushes.Transparent;
            if (!d.Contains("MenuBevelDarkBrush")) d["MenuBevelDarkBrush"] = Brushes.Transparent;
            if (!d.Contains("MenuBevel2LightBrush")) d["MenuBevel2LightBrush"] = Brushes.Transparent;
            if (!d.Contains("MenuBevel2DarkBrush")) d["MenuBevel2DarkBrush"] = Brushes.Transparent;
            if (!d.Contains("MenuBevelLightThickness")) d["MenuBevelLightThickness"] = new Thickness(0);
            if (!d.Contains("MenuBevelDarkThickness")) d["MenuBevelDarkThickness"] = new Thickness(0);
            if (!d.Contains("MenuBevel2LightThickness")) d["MenuBevel2LightThickness"] = new Thickness(0);
            if (!d.Contains("MenuBevel2DarkThickness")) d["MenuBevel2DarkThickness"] = new Thickness(0);
            if (!d.Contains("MenuBevelInnerMargin")) d["MenuBevelInnerMargin"] = new Thickness(0);
            if (!d.Contains("FooterBevelDarkBrush")) d["FooterBevelDarkBrush"] = Brushes.Transparent;
            if (!d.Contains("FooterBevelLightBrush")) d["FooterBevelLightBrush"] = Brushes.Transparent;
            if (!d.Contains("FooterCellLightThickness")) d["FooterCellLightThickness"] = new Thickness(0);
            if (!d.Contains("FooterCellDarkThickness")) d["FooterCellDarkThickness"] = new Thickness(0);
            if (!d.Contains("FooterCellMargin")) d["FooterCellMargin"] = new Thickness(0);
            if (!d.Contains("FooterCellPadding")) d["FooterCellPadding"] = new Thickness(0);
            if (!d.Contains("DialogHaloMargin")) d["DialogHaloMargin"] = new Thickness(10);
            if (!d.Contains("AboutCaptionVisibility")) d["AboutCaptionVisibility"] = Visibility.Collapsed;
            if (!d.Contains("AboutModernCloseVisibility")) d["AboutModernCloseVisibility"] = Visibility.Visible;
            if (!d.Contains("ShortcutCaptionVisibility")) d["ShortcutCaptionVisibility"] = Visibility.Collapsed;
            if (!d.Contains("ShortcutModernHeaderVisibility")) d["ShortcutModernHeaderVisibility"] = Visibility.Visible;
            if (!d.Contains("AboutShadowOpacity")) d["AboutShadowOpacity"] = 0.6d;
            if (!d.Contains("ShortcutShadowOpacity")) d["ShortcutShadowOpacity"] = 0.6d;
            d["DialogShadowOpacity"] = square ? 0d : 0.72d;
            if (!d.Contains("CheckSunkenDarkThickness")) d["CheckSunkenDarkThickness"] = new Thickness(0);
            if (!d.Contains("CheckSunkenLightThickness")) d["CheckSunkenLightThickness"] = new Thickness(0);
            if (!d.Contains("CheckBoxCheckedBrush")) d["CheckBoxCheckedBrush"] = d["PrimaryBrush"];
            if (!d.Contains("CheckMarkBrush")) d["CheckMarkBrush"] = d["OnPrimaryBrush"];

        }
    }
}
