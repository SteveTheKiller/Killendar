using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Xunit;

namespace Killendar.Tests
{
    public sealed class HeadingAccentTests
    {
        [Fact]
        public void HeadingFollowsLogoChangesWithoutChangingOrdinaryAccentText()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var root = (StackPanel)XamlReader.Parse(@"
<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
 xmlns:theme='clr-namespace:KillerFamily.Theming;assembly=Killendar'>
 <StackPanel.Resources>
  <SolidColorBrush x:Key='AccentLogo' Color='#DD504B'/>
  <SolidColorBrush x:Key='PrimaryBrush' Color='#FF7C77'/>
 </StackPanel.Resources>
 <TextBlock Text='October 2026' FontSize='18' theme:HeadingAccent.Enabled='True'/>
 <TextBlock Text='Ordinary accent text' Foreground='{DynamicResource PrimaryBrush}'/>
</StackPanel>");
                    var heading = (TextBlock)root.Children[0];
                    var ordinary = (TextBlock)root.Children[1];
                    root.Measure(new Size(400, 100));
                    root.Arrange(new Rect(0, 0, 400, 100));
                    Assert.Equal(Color.FromRgb(221, 80, 75), ((SolidColorBrush)heading.Foreground).Color);
                    Assert.Equal(Color.FromRgb(255, 124, 119), ((SolidColorBrush)ordinary.Foreground).Color);
                    Assert.NotNull(heading.Effect);
                    Assert.Null(ordinary.Effect);
                    var originalSize = heading.DesiredSize;

                    root.Resources["AccentLogo"] = new SolidColorBrush(Color.FromRgb(147, 26, 26));
                    root.UpdateLayout();
                    Assert.Equal(Color.FromRgb(147, 26, 26), ((SolidColorBrush)heading.Foreground).Color);
                    Assert.Equal(Color.FromRgb(255, 124, 119), ((SolidColorBrush)ordinary.Foreground).Color);
                    Assert.Equal(originalSize, heading.DesiredSize);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Heading layout did not complete.");
            if (failure != null) throw failure;
        }
    }
}
