using System;
using System.Threading;
using System.Windows.Controls;
using KillerFamily.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The strip's lower rule only shows when rows follow it; a card ending at the strip
    /// drew a stray divider along its bottom edge.</summary>
    public sealed class QuickStripRuleTests
    {
        [Fact]
        public void StripEndsTheCardOnlyWhenNothingFollows()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var menu = new ContextMenu();
                    var strip = CardBuilder.QuickStrip("t", Array.Empty<CardAction>());
                    menu.Items.Add(strip);
                    Assert.True(CardBuilder.EndsCard(strip));
                    menu.Items.Add(new MenuItem { Header = "row" });
                    Assert.False(CardBuilder.EndsCard(strip));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
