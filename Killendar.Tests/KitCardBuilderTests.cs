using System.Linq;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using KillerFamily.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>CT-004 and CT-005: card slots are not announced as menu items, and a disabled quick
    /// button still shows why.</summary>
    [Collection("Calendar settings")]
    public sealed class KitCardBuilderTests
    {
        private static CardAction Action(bool enabled) =>
            new CardAction("G", "Cap", "Delete", "Ctrl+Delete", () => { }, enabled);
        private static CardChoice Choice() => new CardChoice("A", "Tip A", null, false, () => { }, false);

        [Fact]
        public void SlotsAreNotAnnouncedAsMenuItems() => KitTestHost.Sta(() =>
        {
            var slots = new[]
            {
                CardBuilder.Header("Title", "meta"),
                CardBuilder.QuickStrip("Quick", new[] { Action(true) }),
                CardBuilder.Labeled("Label", new TextBlock()),
            };
            foreach (var slot in slots)
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(slot);
                Assert.NotEqual(AutomationControlType.MenuItem, peer.GetAutomationControlType());
            }
            var row = CardBuilder.Row("Open", null, null, () => { });
            Assert.Equal(AutomationControlType.MenuItem, UIElementAutomationPeer.CreatePeerForElement(row).GetAutomationControlType());
        });

        [Fact]
        public void ADisabledQuickButtonStillShowsItsTooltip() => KitTestHost.Sta(() =>
        {
            var strip = CardBuilder.QuickStrip("Quick", new[] { Action(false) });
            var button = (Button)((System.Windows.Controls.Primitives.UniformGrid)((Border)strip.Header).Child).Children[0];
            Assert.False(button.IsEnabled);
            Assert.True(ToolTipService.GetShowOnDisabled(button));

            var toggles = CardBuilder.ToggleRow("T", new[] { Choice() });
            var toggle = Find<ToggleButton>(toggles);
            Assert.True(ToolTipService.GetShowOnDisabled(toggle));
        });

        private static T Find<T>(MenuItem slot) where T : System.Windows.DependencyObject
        {
            var queue = new System.Collections.Generic.Queue<object>();
            queue.Enqueue(slot.Header);
            while (queue.Count > 0)
            {
                var o = queue.Dequeue();
                if (o is T hit) return hit;
                if (o is System.Windows.DependencyObject d)
                    foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(d)) queue.Enqueue(c);
            }
            throw new System.InvalidOperationException("not found");
        }
    }
}
