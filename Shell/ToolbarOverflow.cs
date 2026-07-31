using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

// ============================================================
// TOOLBAR OVERFLOW
//
// The toolbar is a 3-column Grid - actions/prev/Today/period on the left, the next arrow riding
// the flexible middle, the four view buttons pinned right - and nothing in it used to shed when
// the row got narrow. Open the appointment panel on anything but a wide window and the buttons
// ran off the right edge, cut in half; narrower still and the period label itself was sliced,
// which is the one thing that says WHERE YOU ARE. (Steve, 2026-07-30.)
//
// So the bar sheds, in a fixed order, into one "..." menu. What is shed is always reachable -
// nothing is ever merely hidden.
//
// SHED ORDER (first to go, last to go). The rule is: the further something is from "what am I
// looking at", the earlier it goes.
//   1. the four view buttons   - a whole group, and the biggest single win
//   2. Export .ics             - rarest action
//   3. Import .ics
//   4. Today                   - the arrows still navigate without it
//   5. New                     - the primary action, so it holds on longest
// Prev, Next and the period label NEVER shed. They are the answer to "where am I and how do I
// move", and a calendar with neither is not a calendar.
//
// ------------------------------------------------------------
// THE RULE THIS FILE EXISTS TO OBEY: never call Measure() from a layout event.
//
// The first version called Measure(infinity) inside the SizeChanged handler. That starts a layout
// pass from inside a layout pass: WPF re-measures with the real constraint straight afterwards,
// which raises SizeChanged again, which measures again. The toolbar flickered continuously and the
// loop pinned the UI thread, which made every other interaction feel laggy. (Steve, 2026-07-30:
// "blinking on and off like a disco ball".)
//
// Nothing here measures. It reads ActualWidth - a value layout has already computed - caches each
// item's width from a frame where that item was visible, and runs dispatched at Loaded priority,
// coalesced behind a flag.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private bool _overflowUpdateQueued;

        /// <summary>Room to leave for the next arrow, which has its own Auto column and belongs to
        /// no shed group, plus a little breathing space.</summary>
        private const double NextArrowAllowance = 60;

        /// <summary>
        /// The narrowest the date label may be squeezed before the bar sheds instead. The label
        /// lives in the star column and trims rather than pushing anything off, so without a floor
        /// it would silently ellipsise down to "Wed..." while four buttons sat beside it at full
        /// width. Shedding a button the user is not looking at beats destroying the one label that
        /// says where they are.
        /// </summary>
        private const double PeriodMinWidth = 120;

        /// <summary>
        /// Dead band around each switch point. Shedding an item frees exactly its width, which
        /// immediately satisfies the "does it fit" test and brings it straight back. Without a gap
        /// between the hide threshold and the show threshold the bar flips every frame.
        /// </summary>
        private const double OverflowHysteresis = 24;

        /// <summary>Width the "..." button occupies once anything has been shed into it.</summary>
        private const double OverflowButtonWidth = 32;

        /// <summary>One shed-able item, in shed order.</summary>
        private sealed class ShedItem
        {
            public FrameworkElement Element = null!;
            public double Width;                 // cached from a frame where it was visible
            public bool Shed;
            public Func<IEnumerable<MenuItem>> MenuItems = null!;   // what it contributes to "..."
        }

        private List<ShedItem> _shed = null!;

        /// <summary>Wire the fit test. Called once from the ctor, after InitToolbarStyle.</summary>
        private void InitToolbarOverflow()
        {
            // Declaration order IS shed order - see the header.
            _shed = new List<ShedItem>
            {
                // No glyph literals here. One() looks the glyph up in ToolbarStyle.cs's
                // ToolbarItems() - the same table that draws the button - so a codepoint can only
                // ever be changed in one place. Hardcoding them here meant that moving Today's
                // glyph left the overflow menu still drawing the old one. (Steve, 2026-07-30.)
                new ShedItem { Element = ViewTabs,     MenuItems = ViewMenuItems },
                new ShedItem { Element = ExportBtn,    MenuItems = () => One(ExportBtn,   ExportBtn_Click) },
                new ShedItem { Element = ImportBtn,    MenuItems = () => One(ImportBtn,   ImportBtn_Click) },
                new ShedItem { Element = TodayBtn,     MenuItems = () => One(TodayBtn,    TodayBtn_Click) },
                new ShedItem { Element = NewEventBtn,  MenuItems = () => One(NewEventBtn, NewEventBtn_Click) },
            };

            RebuildOverflowMenu();
            // SizeChanged covers the panel opening, the window resizing and a display-mode switch,
            // which is every case that can change the fit. The handler only QUEUES work.
            ToolbarGrid.SizeChanged += (_, _) => QueueToolbarOverflowUpdate();
        }

        private void ViewOverflowBtn_Click(object sender, RoutedEventArgs e)
        {
            RebuildOverflowMenu();
            ViewOverflowMenu.PlacementTarget = ViewOverflowBtn;
            ViewOverflowMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ViewOverflowMenu.IsOpen = true;
        }

        /// <summary>Schedule one fit test after the current layout pass. Coalesced: a resize drag
        /// raises SizeChanged dozens of times a second and this runs at most once per frame.</summary>
        private void QueueToolbarOverflowUpdate()
        {
            if (_overflowUpdateQueued) return;
            _overflowUpdateQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _overflowUpdateQueued = false;
                UpdateToolbarOverflow();
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Shed items until the bar fits, or bring them back while there is room, from already
        /// computed widths only.
        /// </summary>
        private void UpdateToolbarOverflow()
        {
            if (ToolbarGrid == null || _shed == null || ViewOverflowBtn == null) return;

            double available = ToolbarGrid.ActualWidth;
            if (available <= 0) return;

            // Cache each visible item's width. A collapsed element reports 0, so only ever record
            // from a frame where it is actually showing.
            foreach (var s in _shed)
                if (!s.Shed && s.Element.ActualWidth > 0) s.Width = s.Element.ActualWidth;

            // The part that never sheds: whatever the left group still holds beyond the shed items.
            double fixedPart = ToolbarLeft.ActualWidth;
            foreach (var s in _shed)
                if (!s.Shed && s.Element != ViewTabs) fixedPart -= s.Width;
            if (fixedPart < 0) fixedPart = 0;

            bool anyUncached = false;
            foreach (var s in _shed) if (s.Width <= 0) anyUncached = true;
            if (anyUncached && !AnyShed()) return;   // wait for a frame with real numbers

            // + PeriodMinWidth: the date label is no longer inside ToolbarLeft, it has its own
            // flexible column, so its floor has to be booked here or the bar never sheds and the
            // label just trims away instead.
            double needed = fixedPart + NextArrowAllowance + PeriodMinWidth;
            foreach (var s in _shed) if (!s.Shed) needed += s.Width;
            // The "..." button itself takes room the moment anything sheds, and it lives in the
            // right-hand column where it competes with the same space. Count it or the first shed
            // frees slightly less than it appears to.
            if (AnyShed()) needed += OverflowButtonWidth;

            bool changed = false;

            // Too wide: shed in declaration order until it fits.
            foreach (var s in _shed)
            {
                if (needed <= available) break;
                if (s.Shed) continue;
                s.Shed = true;
                s.Element.Visibility = Visibility.Collapsed;
                needed -= s.Width;
                changed = true;
            }

            // Room to spare: bring back in REVERSE order, so the last thing shed is the first thing
            // restored. Hysteresis on the way back, or an item returns into the exact width it just
            // freed and the bar oscillates.
            for (int i = _shed.Count - 1; i >= 0; i--)
            {
                var s = _shed[i];
                if (!s.Shed || s.Width <= 0) continue;
                if (needed + s.Width + OverflowHysteresis > available) break;
                s.Shed = false;
                s.Element.Visibility = Visibility.Visible;
                needed += s.Width;
                changed = true;
            }

            if (!changed) return;

            ViewOverflowBtn.Visibility = AnyShed() ? Visibility.Visible : Visibility.Collapsed;
            RebuildOverflowMenu();
        }

        private bool AnyShed()
        {
            foreach (var s in _shed) if (s.Shed) return true;
            return false;
        }

        /// <summary>
        /// Drop the cached widths and re-test. Call after anything that changes how wide the
        /// buttons are: a display-mode switch or a language change.
        /// </summary>
        private void InvalidateToolbarOverflow()
        {
            if (_shed == null) return;
            foreach (var s in _shed)
            {
                s.Width = 0;
                // Show everything again so the next frame can record natural widths. Anything that
                // still does not fit is shed one frame later.
                s.Shed = false;
                s.Element.Visibility = Visibility.Visible;
            }
            ViewOverflowBtn.Visibility = Visibility.Collapsed;
            QueueToolbarOverflowUpdate();
        }

        // ---- the "..." menu ----

        private void RebuildOverflowMenu()
        {
            if (ViewOverflowMenu == null || _shed == null) return;
            ViewOverflowMenu.Items.Clear();
            bool first = true;
            foreach (var s in _shed)
            {
                if (!s.Shed) continue;
                if (!first) ViewOverflowMenu.Items.Add(new Separator());
                foreach (var mi in s.MenuItems()) ViewOverflowMenu.Items.Add(mi);
                first = false;
            }
        }

        /// <summary>The four view entries, carrying their own glyphs and shortcut keys.</summary>
        private IEnumerable<MenuItem> ViewMenuItems()
        {
            foreach (var btn in new[] { TabMonth, TabWeek, TabDay, TabAgenda })
            {
                // Tag carries the view name and ViewTab_Click reads it - reuse both rather than
                // restating the list, so the menu can never drift from the buttons.
                var tag = btn.Tag as string;
                var mi = new MenuItem
                {
                    Header = ToolbarButtonLabel(btn),
                    Tag = tag,
                    InputGestureText = tag switch
                    {
                        "Month" => "M", "Week" => "W", "Day" => "D", "Agenda" => "A", _ => "",
                    },
                };
                mi.Icon = GlyphFor(btn);
                mi.Click += (_, _) => { if (tag != null) _calendar.SelectView(tag); };
                yield return mi;
            }
        }

        /// <summary>A single shed button as a menu entry, same caption, glyph and action. The glyph
        /// comes from ToolbarItems(), never a literal - see the note in InitToolbarOverflow.</summary>
        private IEnumerable<MenuItem> One(Button btn, RoutedEventHandler click)
        {
            var mi = new MenuItem { Header = ToolbarButtonLabel(btn) };
            mi.Icon = GlyphFor(btn);
            mi.Click += (s, e) => click(btn, e);
            yield return mi;
        }

        /// <summary>The glyph ToolbarStyle.cs draws on this button, as a menu icon. Null if the
        /// button is not in the table, which cannot happen for anything shed-able.</summary>
        private TextBlock? GlyphFor(Button btn)
        {
            foreach (var (b, glyph, _) in ToolbarItems())
                if (ReferenceEquals(b, btn)) return Views.CalendarChrome.MenuGlyph(glyph);
            return null;
        }

        /// <summary>
        /// A toolbar button's caption as text. In icon-only modes Content is a TextBlock holding a
        /// glyph, which would put a private-use character in the menu, so fall back to the tooltip -
        /// ToolbarStyle.cs sets it to the localized name in every mode.
        /// </summary>
        private static string ToolbarButtonLabel(Button btn) => btn.Content as string
            ?? btn.ToolTip as string
            ?? (btn.Tag as string ?? "");

        /// <summary>Rebuild the menu after a language change or a toolbar mode switch.</summary>
        private void RefreshViewOverflowMenu() => RebuildOverflowMenu();
    }
}
