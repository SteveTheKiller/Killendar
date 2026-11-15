using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using KillerFamily.Theming;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GC-002 and GC-003: the yellow-text outline does not tax every TextBlock on themes that
    /// never draw it, and reads the DPI scale of the text it outlines.</summary>
    [Collection("Calendar settings")]
    public sealed class KitAccentShadowTests
    {
        private static readonly SolidColorBrush Yellow = new SolidColorBrush(Color.FromRgb(0xF2, 0xC5, 0x00));

        private static TextBlock Lay(TextBlock text)
        {
            AccentShadow.Initialize();
            var panel = new StackPanel();
            panel.Children.Add(text);
            panel.Measure(new Size(300, 100));
            panel.Arrange(new Rect(0, 0, 300, 100));
            panel.UpdateLayout();
            return text;
        }

        [Fact]
        public void TextCarriesNoEffectBindingWhileTheThemeWantsNoOutline() => KitTestHost.Sta(() =>
        {
            var res = Application.Current.Resources;
            res["PrimaryBrush"] = Yellow;
            res["WordmarkAccentShadowOpacity"] = 0.0;
            try
            {
                var text = Lay(new TextBlock { Text = "Plain", Foreground = Yellow });
                Assert.False(BindingOperations.IsDataBound(text, UIElement.EffectProperty),
                    "A theme with no outline should not bind an Effect on every TextBlock.");
            }
            finally { res.Remove("PrimaryBrush"); res.Remove("WordmarkAccentShadowOpacity"); }
        }, needApp: true);

        [Fact]
        public void RefreshOutlinesTextThatWaitedForAThemeThatWantsIt() => KitTestHost.Sta(() =>
        {
            var res = Application.Current.Resources;
            res["PrimaryBrush"] = Yellow;
            res["WordmarkAccentShadowOpacity"] = 0.0;
            try
            {
                var text = Lay(new TextBlock { Text = "Waiting", Foreground = Yellow });
                res["WordmarkAccentShadowOpacity"] = 0.9;
                KitTestHost.CallStatic(typeof(AccentShadow), "Refresh");
                if (!SystemParameters.HighContrast)
                    Assert.IsType<AccentShadow.Outline>(text.Effect);
            }
            finally { res.Remove("PrimaryBrush"); res.Remove("WordmarkAccentShadowOpacity"); }
        }, needApp: true);

        [Fact]
        public void TheOutlineReadsTheDpiOfItsOwnText() => KitTestHost.Sta(() =>
        {
            var res = Application.Current.Resources;
            res["PrimaryBrush"] = Yellow;
            res["WordmarkAccentShadowOpacity"] = 0.9;
            try
            {
                var text = Lay(new TextBlock { Text = "Scale", Foreground = Yellow });
                var binding = BindingOperations.GetBinding(text, UIElement.EffectProperty);
                Assert.NotNull(binding);
                Assert.IsType<TextBlock>(binding!.ConverterParameter);
            }
            finally { res.Remove("PrimaryBrush"); res.Remove("WordmarkAccentShadowOpacity"); }
        }, needApp: true);
    }
}
