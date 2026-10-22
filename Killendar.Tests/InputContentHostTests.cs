using System;
using System.Threading;
using System.Windows;
using KillerFamily.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>98SE's compact inset replaces a field's padding, but room a field reserves on one
    /// side for an overlay, such as the file dialog's recent-locations arrow, survives it.</summary>
    public sealed class InputContentHostTests
    {
        [Fact]
        public void CompactInsetKeepsRoomReservedForAnOverlay()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var compact = new Thickness(2, 1, 2, 1);
                    var plain = new InputContentHost { Padding = new Thickness(8, 0, 8, 0), CompactPadding = compact };
                    Assert.Equal(compact, plain.Padding);
                    var path = new InputContentHost { Padding = new Thickness(8, 0, 28, 0), CompactPadding = compact };
                    Assert.Equal(new Thickness(2, 1, 22, 1), path.Padding);
                    var leading = new InputContentHost { Padding = new Thickness(30, 0, 6, 0), CompactPadding = compact };
                    Assert.Equal(new Thickness(26, 1, 2, 1), leading.Padding);
                    path.CompactPadding = null;
                    Assert.Equal(new Thickness(8, 0, 28, 0), path.Padding);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
