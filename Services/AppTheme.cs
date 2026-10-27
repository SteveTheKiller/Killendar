using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Killendar.Services
{
    /// <summary>
    /// Killendar's side of the family theme engine. ThemeManager.cs is the KillerUI template and
    /// is never edited here; everything particular to Killendar comes in through its hooks: the
    /// Dark/Red first run, the values in Themes/App, and the calendar tokens Killendar's views
    /// derive (weekend wash, spillover days, month hover, appointment chips).
    /// </summary>
    internal static class AppTheme
    {
        /// <summary>Call once before ThemeManager.Initialize, in App.OnStartup and in test harnesses.
        /// The settings hooks stay with App, which connects them only once the normal app starts.</summary>
        internal static void Configure()
        {
            // Killendar ships DARK + RED. The three neutral families each define their own red
            // (Dark #DD504B, Light #931A1A, Black #FF2929), and the brand wordmark, the og-image and
            // killendar.net are all built on Dark's #DD504B, so Dark is the first-run theme.
            ThemeManager.SetDefaults(Theme.Dark, dark: Accent.Red, light: Accent.Green, black: Accent.Red, se98: Accent.Blue);
            ThemeManager.AppTokens = AppTokens;
            ThemeManager.AppDerivedTokens = AppDerivedTokens;
        }

        private static string FileName(Theme theme) => theme == Theme.SE98 ? "98SE" : theme.ToString();

        // Before the family contract: Killendar's per-theme values, then its accent values, then
        // its defaults for whatever a theme left unsaid.
        private static void AppTokens(ResourceDictionary d, Theme theme, Accent accent)
        {
            Merge(d, $"Themes/App/{FileName(theme)}.xaml", overwrite: true);
            if (ThemeManager.SupportsAccents(theme))
                Merge(d, $"Themes/App/Accents/{FileName(theme)}/{accent}.xaml", overwrite: true);
            Merge(d, "Themes/App/Defaults.xaml", overwrite: false);

            bool se98 = theme == Theme.SE98;
            // Text fields and the date field shell sit on the surface tier unless a theme says otherwise.
            ThemeManager.Mirror(d, "InputFieldBrush", "SurfaceBrush");
            // Sidebar headings are headings too: Lacquer at its regular weight (it has no bold cut).
            // 98SE keeps Courier New bold (the weight is in Themes/App).
            ThemeManager.SetIfAbsent(d, "SidebarHeadingFont", se98 ? new FontFamily("Courier New") : ThemeManager.LacquerHeadingStack);
            // The title bar name sits on the wordmark baseline in Killendar's frame without a shift.
            if (!d.Contains("FileNameTransform"))
            {
                var shift = new TranslateTransform(0, 0);
                shift.Freeze();
                d["FileNameTransform"] = shift;
            }
        }

        private static Color Shade(Color c, double keep) =>
            Color.FromRgb((byte)(c.R * keep), (byte)(c.G * keep), (byte)(c.B * keep));

        private static double Luma(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

        // After the family contract: the calendar tokens, built from family ones.
        private static void AppDerivedTokens(ResourceDictionary d, Theme theme, Accent accent)
        {
            bool se98 = theme == Theme.SE98;
            ThemeManager.Mirror(d, "CalendarViewSelectedBrush", "SelectionBg");
            // The selected view tile is raised: its shadow covers far more area than an icon
            // shadow, so it runs lighter to read at the same weight. 98SE stays flat.
            d["SelectedTileShadowOpacity"] = se98 ? 0d : 0.45d;

            // The classic calendar is a white client area. Give unclassified appointments a
            // pale version of the selected 98SE accent instead of the old face gray, which could
            // disappear into the calendar. Each accent therefore owns its own readable shade.
            if (se98 && d["PrimaryBrush"] is SolidColorBrush primary)
            {
                Color p = primary.Color;
                d["ChipBrush"] = new SolidColorBrush(Color.FromRgb(
                    (byte)(255 * 0.84 + p.R * 0.16),
                    (byte)(255 * 0.84 + p.G * 0.16),
                    (byte)(255 * 0.84 + p.B * 0.16)));
            }
            ThemeManager.Mirror(d, "ChipBrush", "RowHoverBrush");

            // Calendar structure is intentionally only TWO tones: every weekday shares the base
            // pane, and Saturday/Sunday receive one dark wash. Earlier versions alternated accent
            // strengths by display column, which arbitrarily made Tuesday and Thursday dark and
            // produced five apparent shades once selection/today were added.
            SolidColorBrush? calendarAccent = theme == Theme.Ectoplasm
                ? d["InputBorderBrush"] as SolidColorBrush
                : d["PrimaryBrush"] as SolidColorBrush;
            if (calendarAccent != null)
            {
                byte weekend = se98 ? (byte)24 : (byte)56;
                d["CalendarEvenColumnBrush"] = Brushes.Transparent;
                d["CalendarOddColumnBrush"] = Brushes.Transparent;
                d["CalendarWeekendBrush"] = new SolidColorBrush(Color.FromArgb(weekend, 0, 0, 0));
                // Month hover. Where the theme's hover gray sits on its weekend gray (Light), a
                // hovered weekday turned the weekend color and a hovered weekend did not change.
                // There the hover takes the tone halfway between the weekday and weekend grays,
                // and a hovered weekend day moves the same step further.
                if (d["PaneBrush"] is SolidColorBrush monthPane && monthPane.Color.A == 255 &&
                    d["RowHoverBrush"] is SolidColorBrush rowHover)
                {
                    Color weekendColor = Shade(monthPane.Color, 1 - weekend / 255.0);
                    if (Math.Abs(Luma(rowHover.Color) - Luma(weekendColor)) < 16)
                    {
                        double half = 1 - weekend / 510.0;
                        d["CalendarDayHoverBrush"] = new SolidColorBrush(Shade(monthPane.Color, half));
                        d["CalendarWeekendHoverBrush"] = new SolidColorBrush(Shade(weekendColor, half));
                    }
                }
                // Spillover dates are a different state, not another accent stripe. Give them an
                // opaque dark client color so none of the weekday tint can bleed through.
                Color outsideBase = d["PaneBrush"] is SolidColorBrush pane
                    ? pane.Color : Color.FromRgb(42, 42, 42);
                double outsideScale = se98 ? 0.48 : 0.38;
                d["CalendarOutsideMonthBrush"] = new SolidColorBrush(Color.FromRgb(
                    (byte)(outsideBase.R * outsideScale),
                    (byte)(outsideBase.G * outsideScale),
                    (byte)(outsideBase.B * outsideScale)));
                d["CalendarEvenHeaderBrush"] = Brushes.Transparent;
                d["CalendarOddHeaderBrush"] = Brushes.Transparent;
                d["CalendarWeekendHeaderBrush"] = new SolidColorBrush(
                    Color.FromArgb(se98 ? (byte)16 : (byte)36, 0, 0, 0));
            }
            ThemeManager.Mirror(d, "CalendarDayHoverBrush", "RowHoverBrush");
            ThemeManager.Mirror(d, "CalendarWeekendHoverBrush", "RowHoverBrush");

            // Appointments without a category take the accent.
            d["KsCatAppt"] = d["PrimaryBrush"];
        }

        private static void Merge(ResourceDictionary d, string path, bool overwrite)
        {
            ResourceDictionary source;
            try { source = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{path}") }; }
            catch (IOException) { return; }   // no file for this theme or accent
            foreach (object key in source.Keys)
                if (overwrite || !d.Contains(key)) d[key] = source[key];
        }
    }
}
