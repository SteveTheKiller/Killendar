using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>
    /// Writes every resolved theme resource for every theme and accent, one "theme/accent key value"
    /// line each, so a theme refactor can be compared value by value before and after. Not a check:
    /// it produces a file and passes or fails nothing. It runs only when KILLENDAR_THEME_DUMP names
    /// the output file, builds every palette off screen and opens no window.
    /// </summary>
    public sealed class ThemeDumpTests
    {
        [Fact]
        public void DumpEveryThemeAndAccent()
        {
            string? path = Environment.GetEnvironmentVariable("KILLENDAR_THEME_DUMP");
            if (string.IsNullOrEmpty(path)) return;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { Dump(path!); }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromMinutes(5)), "The theme dump did not finish.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private static void Dump(string path)
        {
            const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var assembly = typeof(ThemeManager).Assembly;
            // pack://application:,,,/ resolves against the resource assembly, which under the test
            // host is not Killendar until it is pointed there.
            typeof(Application).GetField("_resourceAssembly", any)!.SetValue(null, assembly);
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Controls/AppStyles.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            // After the move the app configures the family engine first; before it there is no hook.
            assembly.GetType("Killendar.Services.AppTheme")?.GetMethod("Configure", any)?.Invoke(null, null);

            var create = typeof(ThemeManager).GetMethod("CreateDictionary", any);
            var load = typeof(ThemeManager).GetMethod("LoadDict", any)!;
            var lines = new List<string>();
            foreach (Theme theme in Enum.GetValues(typeof(Theme)))
                foreach (Accent accent in Enum.GetValues(typeof(Accent)))
                {
                    typeof(ThemeManager).GetField("_current", any)!.SetValue(null, theme);
                    if (create != null)
                    {
                        var built = (ResourceDictionary)create.Invoke(null, new object[] { theme, accent })!;
                        app.Resources.MergedDictionaries[0] = built;
                    }
                    else
                    {
                        // The engine before the move reads the accent from its per-family field.
                        string field = theme == Theme.Light ? "_lightAccent" : theme == Theme.Black ? "_blackAccent"
                            : theme == Theme.SE98 ? "_se98Accent" : "_darkAccent";
                        typeof(ThemeManager).GetField(field, any)!.SetValue(null, accent);
                        load.Invoke(null, new object[] { theme });
                    }
                    var keys = new SortedSet<string>(StringComparer.Ordinal);
                    void Collect(ResourceDictionary d)
                    {
                        foreach (var key in d.Keys) if (key is string s && !s.StartsWith("Str_", StringComparison.Ordinal)) keys.Add(s);
                        foreach (var child in d.MergedDictionaries) Collect(child);
                    }
                    foreach (var d in app.Resources.MergedDictionaries) Collect(d);
                    foreach (string key in keys)
                        lines.Add(theme + "/" + accent + " " + key + " " + Describe(app.TryFindResource(key)));
                }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static string Describe(object? value) => value switch
        {
            null => "null",
            SolidColorBrush b => "brush " + b.Color + " o" + F(b.Opacity),
            GradientBrush g => g.GetType().Name + " " + string.Join(",", g.GradientStops.Select(s => s.Color + "@" + F(s.Offset))) + " o" + F(g.Opacity),
            Color c => "color " + c,
            double d => "double " + F(d),
            bool b => "bool " + b,
            Thickness t => "thickness " + t,
            CornerRadius r => "radius " + r,
            GridLength l => "gridlength " + l,
            FontWeight w => "weight " + w,
            Visibility v => "visibility " + v,
            Transform t => "transform " + t.Value,
            DropShadowEffect e => "shadow " + e.Color + " b" + F(e.BlurRadius) + " d" + F(e.ShadowDepth) + " a" + F(e.Direction) + " o" + F(e.Opacity),
            string s => "string " + s.Replace("\n", "\\n"),
            FontFamily f => "font " + f.Source,
            Style s => "style " + s.TargetType?.Name + " " + s.Setters.Count,
            _ => value.GetType().Name,
        };

        private static string F(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
