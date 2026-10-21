using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using KillerFamily.Accessibility;

namespace Killendar.Views
{
    /// <summary>
    /// Keyboard and screen-reader access to the parts of the calendar that are not appointments:
    /// a day (Month cell, Week/Day column header, Agenda heading) and the empty time slots of a
    /// Week/Day column. The mouse gestures stay exactly as they were; these give the same two
    /// actions a key and an automation pattern. A day opens its agenda; a slot creates there.
    ///
    /// Every day stays an ordinary Tab stop, followed by its own appointment chips: making a grid
    /// one Tab stop would have shut the chips inside it out of the Tab order. The arrows are the
    /// quick way across, and Enter or Space opens a day, like the date picker and the chips.
    /// </summary>
    internal static class CalendarKeyboard
    {
        /// <summary>The day an arrow moves to, or null for any other key. <paramref name="rows"/>
        /// is false for a single row of days, where Up and Down have nowhere to go.</summary>
        internal static DateTime? DayTarget(DateTime day, Key key, bool rows) => key switch
        {
            Key.Left  => day.AddDays(-1),
            Key.Right => day.AddDays(1),
            Key.Up when rows   => day.AddDays(-7),
            Key.Down when rows => day.AddDays(7),
            _ => null,
        };

        /// <summary>The slot a key moves to, in minutes after midnight, or null for any other key.
        /// Up/Down move one visible subdivision, PageUp/PageDown an hour, Home/End to the ends of
        /// the day; the last slot starts one subdivision before midnight, as a click's does.</summary>
        internal static int? SlotTarget(int minutes, Key key, int snap)
        {
            int last = 24 * 60 - snap;
            int? next = key switch
            {
                Key.Up       => minutes - snap,
                Key.Down     => minutes + snap,
                Key.PageUp   => minutes - 60,
                Key.PageDown => minutes + 60,
                Key.Home     => 0,
                Key.End      => last,
                _ => null,
            };
            if (next == null) return null;
            return Math.Max(0, Math.Min(last, next.Value / snap * snap));
        }

        /// <summary>
        /// Makes <paramref name="day"/> a focusable, named day. Arrow keys look for a sibling day
        /// in the same panel and move focus to it; a day outside the panel (the next month) is
        /// simply not there, so focus stays put.
        /// </summary>
        internal static void MakeDay(DayBorder day, DateTime date, bool rows)
        {
            day.Tag = date.Date;
            day.Focusable = true;
            day.FocusVisualStyle = AccessibleControls.FocusStyleFor(day);
            day.SizeChanged += (_, _) => day.FocusVisualStyle = AccessibleControls.FocusStyleFor(day);
            AutomationProperties.SetName(day, date.ToString("D"));
            day.KeyDown += (_, e) =>
            {
                // Only the day's own keys: a chip inside the cell handles its own Enter.
                if (e.OriginalSource != day) return;
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    day.Open();
                    return;
                }
                if (DayTarget(date.Date, e.Key, rows) is not DateTime target || day.Parent is not Panel siblings) return;
                e.Handled = true;
                siblings.Children.OfType<DayBorder>().FirstOrDefault(d => d.Tag is DateTime t && t == target)?.Focus();
            };
        }

        /// <summary>A day surface: a Border, so the views keep laying it out and painting it as
        /// one, with a button's automation peer so a screen reader can name it and open it.</summary>
        internal sealed class DayBorder(Action open) : Border
        {
            internal void Open() => open();

            protected override AutomationPeer OnCreateAutomationPeer() => new DayPeer(this);

            private sealed class DayPeer(DayBorder owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
            {
                protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
                protected override string GetClassNameCore() => nameof(DayBorder);

                public override object GetPattern(PatternInterface patternInterface) =>
                    patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

                void IInvokeProvider.Invoke()
                {
                    if (!IsEnabled()) throw new ElementNotEnabledException();
                    ((DayBorder)Owner).Open();
                }
            }
        }

        /// <summary>
        /// A Week/Day column. It keeps a current slot for the keyboard, named with the date and
        /// time so a screen reader says where Enter would create, and its Invoke creates there.
        /// </summary>
        internal sealed class SlotCanvas(DateTime date, Action<DateTime> create) : Canvas
        {
            /// <summary>Minutes after midnight of the keyboard's slot.</summary>
            internal int Current { get; set; } = 9 * 60;

            internal DateTime Date => date.Date;

            internal void Create() => create(date.Date.AddMinutes(Current));

            internal void Announce()
            {
                AutomationProperties.SetName(this,
                    date.ToString("D") + ", " + Services.DateFormatManager.Time(date.Date.AddMinutes(Current)));
                if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                    UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }

            protected override AutomationPeer OnCreateAutomationPeer() => new SlotPeer(this);

            private sealed class SlotPeer(SlotCanvas owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
            {
                protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
                protected override string GetClassNameCore() => nameof(SlotCanvas);

                public override object GetPattern(PatternInterface patternInterface) =>
                    patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

                void IInvokeProvider.Invoke()
                {
                    if (!IsEnabled()) throw new ElementNotEnabledException();
                    ((SlotCanvas)Owner).Create();
                }
            }
        }
    }
}
