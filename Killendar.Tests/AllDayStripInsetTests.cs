using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The all-day strip lines up with the day columns: it takes the same scrollbar inset
    /// as the header, or a bar like "Change freeze" sits a scrollbar's width right of its day.</summary>
    [Collection("Calendar settings")]
    public sealed class AllDayStripInsetTests
    {
        [Fact]
        public void AllDayStripTakesTheHeaderInset()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    TestApplication.Ensure();
                    var view = new WeekView { Anchor = new DateTime(2026, 11, 16) };
                    view.ApplyRightInset(17);
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var header = (Grid)typeof(TimeGridView).GetField("_headerRow", flags)!.GetValue(view);
                    var strip = (Grid)typeof(TimeGridView).GetField("_allDayStrip", flags)!.GetValue(view);
                    Assert.Equal(17, header.Margin.Right);
                    Assert.Equal(17, strip.Margin.Right);
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The layout test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
