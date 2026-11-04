using System;
using System.Windows;

namespace Killendar.Services
{
    /// <summary>String lookup for code that can also run without an Application, such as the
    /// settings-save errors the storage tests trigger. Shows the key when nothing is loaded.</summary>
    internal static class UiText
    {
        internal static string Loc(string key) => Application.Current?.TryFindResource(key) as string ?? key;
    }

    /// <summary>The uninstall prompt and the install conflict prompt open before the persistence
    /// hooks are wired. They still read the saved language, but write nothing and leave the hooks
    /// as they were for the rest of startup.</summary>
    internal static class SetupLocale
    {
        internal static void Apply(Action initialize)
        {
            var get = ThemeManager.GetSetting;
            var set = ThemeManager.SetSetting;
            ThemeManager.GetSetting = Settings.Get;
            ThemeManager.SetSetting = (_, _) => { };
            try { initialize(); }
            finally
            {
                ThemeManager.GetSetting = get;
                ThemeManager.SetSetting = set;
            }
        }
    }
}
