using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>CT-001, CT-002, CT-003, GI-004 and GI-005: the shadows and radii in Controls.xaml follow the theme.</summary>
    [Collection("Calendar settings")]
    public sealed class KitControlsXamlTests
    {
        private static string Xaml() => KitTestHost.Source(@"Controls\Controls.xaml");

        private static string Squash(string s) => Regex.Replace(s, @"\s+", " ").Trim();

        private const string CollapseTrigger =
            "<DataTrigger Binding=\"{Binding Effect.Opacity, RelativeSource={RelativeSource Self}}\" Value=\"0\">"
            + " <Setter Property=\"Visibility\" Value=\"Collapsed\"/> </DataTrigger>";

        [Fact]
        public void NoShadowInTheKitHasAFixedOpacity()
        {
            var fixedOnes = Regex.Matches(Xaml(), "<DropShadowEffect[^>]*\\sOpacity=\"[0-9.]+\"")
                .Cast<Match>().Select(m => Squash(m.Value)).ToList();
            Assert.True(fixedOnes.Count == 0, "Shadows with a fixed opacity: " + string.Join(" | ", fixedOnes));
        }

        [Fact]
        public void TheUnusedPaneShadowAndShadowBarResourcesAreGone()
        {
            string xaml = Xaml();
            Assert.DoesNotContain("x:Key=\"PaneShadow\"", xaml);
            Assert.DoesNotContain("x:Key=\"ShadowBar\"", xaml);
        }

        [Fact]
        public void TheDarkComboPopupUsesTheThemesComboShadow()
        {
            var tag = Regex.Match(Xaml(), "<Border x:Name=\"PopupBd\"[^>]*>").Value;
            Assert.True(tag.Length > 0, "The DarkComboBox popup border was not found.");
            Assert.Contains("Effect=\"{DynamicResource ComboPopupShadow}\"", tag);
        }

        [Fact]
        public void TheComboPopupGrainSharesTheCardsRadius()
        {
            Assert.DoesNotContain(
                "<Border IsHitTestVisible=\"False\" CornerRadius=\"{DynamicResource ControlCornerRadius}\" Background=\"{DynamicResource GrainTileBrush}\"",
                Xaml());
        }

        [Fact]
        public void BothMenuShadowLayersDropOutWhenTheThemeHasNoShadow()
        {
            int layers = Regex.Matches(Squash(Xaml()), Regex.Escape(CollapseTrigger)).Count;
            Assert.Equal(2, layers);
        }

        [Fact]
        public void TheCollapseTriggerFollowsTheThemeOpacityAtRuntime() => KitTestHost.Sta(() =>
        {
            string xaml = "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\""
                + " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=mscorlib\""
                + " Width=\"10\" Height=\"10\" Background=\"Black\">"
                + "<Border.Resources><sys:Double x:Key=\"FlyoutShadowOpacity\">0</sys:Double></Border.Resources>"
                + "<Border.Style><Style TargetType=\"Border\"><Style.Triggers>" + CollapseTrigger + "</Style.Triggers></Style></Border.Style>"
                + "<Border.Effect><DropShadowEffect Opacity=\"{DynamicResource FlyoutShadowOpacity}\"/></Border.Effect></Border>";
            var border = (System.Windows.Controls.Border)XamlReader.Parse(xaml);
            Assert.Equal(Visibility.Collapsed, border.Visibility);

            border.Resources["FlyoutShadowOpacity"] = 0.55;
            Assert.Equal(Visibility.Visible, border.Visibility);
        });
    }
}
