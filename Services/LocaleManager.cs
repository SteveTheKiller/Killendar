using System;
using System.Globalization;
using System.Windows;

namespace Killendar.Services
{
    // 10 UI languages. en-US is always the base layer so any locale that omits a key falls back to
    // English; the chosen locale's file layers on top.
    //
    // Append new members at the END: the value is persisted by NAME, not by ordinal, but keeping
    // the order stable also keeps the language menu's order stable.
    public enum Locale { EnUS, Es, ZhTW, ZhCN, Bn, TrTR, De, Fr, Ja, Cs, PlPL }

    public static class LocaleManager
    {
        // Persistence hooks (wired in App.xaml.cs). Default: in-memory only.
        public static Func<string, string?> GetSetting { get; set; } = _ => null;
        public static Action<string, string> SetSetting { get; set; } = (_, _) => { };

        // App.xaml merged-dictionary layout:
        //   [0] theme palette (ThemeManager swaps this one in place)
        //   [1] Controls.xaml
        //   [2] Strings/en-US.xaml   (string BASE - always present)
        //   [3] chosen locale override (added at runtime; absent for English)
        private const int BaseIndex = 2;
        private const int OverrideIndex = 3;

        private static Locale _current = Locale.EnUS;
        public static Locale Current => _current;

        /// <summary>Localized string for a Str_ key. Falls back to the key name when the key is
        /// missing, so a translation gap shows up as itself instead of as blank UI.</summary>
        public static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        /// <summary>Call once at startup (after ThemeManager.Initialize) to restore the saved locale.</summary>
        public static void Initialize()
        {
            _current = Enum.TryParse<Locale>(GetSetting("Locale"), out var l) ? l : Locale.EnUS;
            ApplyInternal(_current);
        }

        /// <summary>Switch locale, persist the choice, and hot-swap the string ResourceDictionary.</summary>
        public static void Apply(Locale locale)
        {
            _current = locale;
            SetSetting("Locale", locale.ToString());
            ApplyInternal(locale);
        }

        private static void ApplyInternal(Locale locale)
        {
            // Resource dictionaries translate fixed interface strings; .NET culture supplies the
            // generated ones (Monday, August, first day of week). Keeping these separate was why
            // choosing English on a Polish Windows installation left Polish calendar headings.
            var culture = CultureFor(locale);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            var merged = Application.Current.Resources.MergedDictionaries;

            // Re-assert the English base so a partial locale falls back to English for missing keys.
            if (merged.Count > BaseIndex)
                merged[BaseIndex] = new ResourceDictionary { Source = new Uri("pack://application:,,,/Strings/en-US.xaml") };

            Uri? overrideUri = locale switch
            {
                Locale.Es   => new Uri("pack://application:,,,/Strings/es.xaml"),
                Locale.Fr   => new Uri("pack://application:,,,/Strings/fr-FR.xaml"),
                Locale.ZhTW => new Uri("pack://application:,,,/Strings/zh-TW.xaml"),
                Locale.ZhCN => new Uri("pack://application:,,,/Strings/zh-CN.xaml"),
                Locale.Bn   => new Uri("pack://application:,,,/Strings/bn.xaml"),
                Locale.TrTR => new Uri("pack://application:,,,/Strings/tr-TR.xaml"),
                Locale.De   => new Uri("pack://application:,,,/Strings/de-DE.xaml"),
                Locale.Ja   => new Uri("pack://application:,,,/Strings/ja-JP.xaml"),
                Locale.Cs   => new Uri("pack://application:,,,/Strings/cs-CZ.xaml"),
                Locale.PlPL => new Uri("pack://application:,,,/Strings/pl-PL.xaml"),
                _           => null,   // English: base only
            };

            if (overrideUri is not null)
            {
                try
                {
                    var ov = new ResourceDictionary { Source = overrideUri };
                    if (merged.Count > OverrideIndex) merged[OverrideIndex] = ov; else merged.Add(ov);
                }
                catch
                {
                    // Locale file not present yet - stay on the English base instead of crashing.
                    if (merged.Count > OverrideIndex) merged.RemoveAt(OverrideIndex);
                }
            }
            else if (merged.Count > OverrideIndex)
            {
                merged.RemoveAt(OverrideIndex);
            }
        }

        internal static CultureInfo CultureFor(Locale locale) => new(locale switch
        {
            Locale.EnUS => "en-US",
            Locale.Es   => "es-ES",
            Locale.ZhTW => "zh-TW",
            Locale.ZhCN => "zh-CN",
            Locale.Bn   => "bn-BD",
            Locale.TrTR => "tr-TR",
            Locale.De   => "de-DE",
            Locale.Fr   => "fr-FR",
            Locale.Ja   => "ja-JP",
            Locale.Cs   => "cs-CZ",
            Locale.PlPL => "pl-PL",
            _           => "en-US",
        });
    }
}
