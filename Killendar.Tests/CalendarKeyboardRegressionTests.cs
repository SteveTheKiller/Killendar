using System;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    // UX-002: empty days and time slots can be reached, named and invoked without a mouse.
    public sealed class CalendarKeyboardRegressionTests
    {
        private static readonly DateTime Wednesday = new(2026, 10, 21);

        [Theory]
        [InlineData(Key.Left, -1)]
        [InlineData(Key.Right, 1)]
        [InlineData(Key.Up, -7)]
        [InlineData(Key.Down, 7)]
        public void ArrowsMoveBetweenDays(Key key, int days)
        {
            Assert.Equal(Wednesday.AddDays(days), CalendarKeyboard.DayTarget(Wednesday, key, rows: true));
        }

        [Fact]
        public void SingleRowOfDaysIgnoresUpAndDown()
        {
            Assert.Null(CalendarKeyboard.DayTarget(Wednesday, Key.Up, rows: false));
            Assert.Equal(Wednesday.AddDays(1), CalendarKeyboard.DayTarget(Wednesday, Key.Right, rows: false));
            Assert.Null(CalendarKeyboard.DayTarget(Wednesday, Key.A, rows: true));
        }

        [Theory]
        [InlineData(540, Key.Down, 30, 570)]
        [InlineData(540, Key.Up, 30, 510)]
        [InlineData(0, Key.Up, 30, 0)]
        [InlineData(1410, Key.Down, 30, 1410)]
        [InlineData(540, Key.PageDown, 15, 600)]
        [InlineData(30, Key.PageUp, 15, 0)]
        [InlineData(540, Key.Home, 30, 0)]
        [InlineData(540, Key.End, 15, 1425)]
        public void SlotKeysStayInsideTheDay(int minutes, Key key, int snap, int expected)
        {
            Assert.Equal(expected, CalendarKeyboard.SlotTarget(minutes, key, snap));
        }

        [Fact]
        public void DaySurfaceIsAFocusableNamedButtonThatInvokes()
        {
            DateTime? opened = null;
            RunSta(() =>
            {
                var day = new CalendarKeyboard.DayBorder(() => opened = Wednesday);
                CalendarKeyboard.MakeDay(day, Wednesday, rows: true);

                Assert.True(day.Focusable);
                Assert.Equal(Wednesday.ToString("D"), AutomationProperties.GetName(day));
                var peer = UIElementAutomationPeer.CreatePeerForElement(day);
                Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            });
            Assert.Equal(Wednesday, opened);
        }

        [Fact]
        public void SlotSurfaceInvokesAtItsCurrentSlot()
        {
            DateTime? created = null;
            RunSta(() =>
            {
                var slots = new CalendarKeyboard.SlotCanvas(Wednesday, at => created = at);
                slots.Current = 14 * 60 + 30;

                var peer = UIElementAutomationPeer.CreatePeerForElement(slots);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            });
            Assert.Equal(Wednesday.AddHours(14.5), created);
        }

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
