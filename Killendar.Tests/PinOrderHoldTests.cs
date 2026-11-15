using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GJ-003: pressing a pinned place held navigation off until the next press if the button came up elsewhere.</summary>
    public sealed class PinOrderHoldTests
    {
        private static bool Holds(PickerPlace? pressed, bool buttonDown) =>
            (bool)KitTestHost.CallStatic(typeof(FileDialog), "PinHoldsSelection", pressed, buttonDown)!;

        [Fact]
        public void TheHoldEndsWithTheButton()
        {
            var place = new PickerPlace("Pinned", @"C:\Pinned", pinned: true);
            Assert.True(Holds(place, true));
            Assert.False(Holds(place, false));
            Assert.False(Holds(null, true));
        }

        [Fact]
        public void PressingAPinnedPlaceNoLongerTouchesTheSharedNavigatingFlag()
        {
            string source = KitTestHost.Source(@"Controls\FileDialog.PinOrder.cs");
            Assert.DoesNotContain("_navigating = true", source);
            Assert.DoesNotContain("_navigating = _pinNavigatingWas", source);
        }
    }
}
