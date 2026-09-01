using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;              // CompositionTarget - the per-frame width ride
using System.Windows.Media.Animation;
using Killendar.Controls;

// The appointment panel's slide. Pure view behavior, so it stays with the window rather than
// moving into the feature: the panel is never hidden with Visibility, the SidebarCol column width
// is animated between 0 and the saved sidebar width instead, so the calendar reflows with the slide rather than
// snapping after it. The rail beside it is permanent, which is the point of a rail.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        /// <summary>Open width of the appointment panel. Matches the fixed content width in XAML.</summary>
        private const double DefaultSidebarW = 330;
        private double _sidebarWidth = DefaultSidebarW;

        /// <summary>
        /// The narrowest the CALENDAR is allowed to get. This, not the window, is the real
        /// constraint - a month grid below about this width has day cells too narrow to hold a
        /// chip, and Week's seven columns collapse into stripes.
        ///
        /// The window's own MinWidth is deliberately much smaller than sidebar + rail + this, so
        /// the app can still be made small when the panel is shut. The panel opening is what has to
        /// respect the calendar's minimum, and it does it by GROWING THE WINDOW rather than by
        /// squeezing the calendar. (2026-07-30)
        /// </summary>
        private const double CalendarMinW = 460;

        // Segoe MDL2 chevrons as escapes so this file stays pure ASCII on disk. The panel is on the
        // LEFT, so closed shows E76C (points right, the direction the calendar gets pushed) and open
        // shows E76B (points left, back into the edge it folds away to).
        private const string ChevronOpen  = "\uE76C";
        private const string ChevronClose = "\uE76B";

        private bool _sidebarOpen;

        private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_sidebarOpen) { CloseSidebar(); return; }

            // Compose a new appointment rather than just sliding the panel open. Opening it bare
            // showed an UNINITIALIZED form: SidebarTitle's XAML default text is Str_Side_New, so
            // it announced "New appointment" while the date and time boxes sat empty and Save
            // could only fail validation. NewAtAnchor fills the fields and calls OpenPanel itself,
            // which lands back in OpenSidebarPanel below - so the slide still happens exactly once.
            _calendar.NewAtAnchor();
        }

        /// <summary>Slides the panel open. Safe when already open - it just refreshes the chrome.</summary>
        private void OpenSidebarPanel()
        {
            // Only animate on a real state change. Re-running the slide every time an appointment is
            // clicked made opening a second one while the panel was already open re-play the whole
            // thing. There is deliberately no opacity fade either: fading the panel renders the
            // subtree to an intermediate surface, which drops the text off ClearType for the
            // duration and snaps it back at the end. The width slide alone is the effect.
            // Grow the window and slide the panel TOGETHER, same duration and easing. Doing the
            // grow first, as a straight assignment, made the calendar snap out to the new full
            // width and then get eaten as the panel slid in over it - the calendar visibly
            // stretched before the panel arrived. In lockstep, the extra window width appears at
            // exactly the rate the panel consumes it, so the calendar simply rides along.
            // (2026-07-30)
            // _sidebarOpen goes true FIRST: EndWidthRide reads it to know it should raise MinWidth
            // to the open minimum once the panel is fully out.
            bool was = _sidebarOpen;
            _sidebarOpen = true;
            Settings.Set("AppointmentSidebarOpen", "1");
            if (!was) { GrowForSidebar(); SlideSidebar(_sidebarWidth); }
            else SidebarSplitter.IsEnabled = true;
            SidebarToggleBtn.Content = ChevronClose;
            SidebarToggleBtn.Tag = "on";                  // lights the rail icon in the accent
            SidebarToggleBtn.ToolTip = Loc("Str_TT_PanelHide");
        }

        /// <summary>Slides the panel shut and drops whatever was being edited.</summary>
        private void CloseSidebar()
        {
            // Same lockstep in reverse: the window gives back the width it borrowed at exactly the
            // rate the panel folds away, so the calendar holds still rather than snapping wider and
            // then being cut down. Without this the window simply stayed wide after a close.
            // _sidebarOpen goes false FIRST: ShrinkAfterSidebar drops MinWidth, and EndWidthRide
            // reads the flag to decide whether to raise it again at the end of the slide.
            bool was = _sidebarOpen;
            _sidebarOpen = false;
            Settings.Set("AppointmentSidebarOpen", "0");
            SidebarSplitter.IsEnabled = false;
            if (was) { ShrinkAfterSidebar(); SlideSidebar(0); }
            SidebarToggleBtn.Content = ChevronOpen;
            SidebarToggleBtn.Tag = null;
            SidebarToggleBtn.ToolTip = Loc("Str_TT_PanelShow");

            _appointments.Clear();
            ResetDayAgenda();               // full close forgets the day agenda (DayAgendaPanel.cs)
            ClearSidebarError();
            // The day marker means "this is what the panel is talking about", so it goes when the
            // panel goes. Today's own fill comes back on its own once nothing is selected.
            _calendar.SetSelection(null, null);
        }

        private void RestoreSidebarPanel()
            => ShowDayAgenda(_calendar.Anchor.Date, null);

        // ---- window width while the panel slides ----
        //
        // The window does NOT get its own animation. Two animations on two clocks - one on a
        // GridLength, one on an HWND's width - never land on the same frame: the OS resize arrives
        // a frame behind the layout, the calendar's star column absorbs the difference, and the
        // calendar visibly breathes for the length of the slide. Subtle, but it is there, and no
        // amount of matching the duration and easing fixes it because the lag is not in the curve.
        // (2026-07-30)
        //
        // Instead the window width is DERIVED from the panel's width every frame:
        //
        //     Width = base + SidebarCol.ActualWidth
        //
        // which makes calendar = Width - panel - rail - slack = base - rail - slack, a constant,
        // by construction. There is nothing left to get out of step.

        private double _rideBase;      // window width with the panel fully shut
        private double _rideGrow;      // how much width the panel is borrowing; 0 = not riding
        private bool _riding;

        /// <summary>Window minimum with the panel shut: rail + calendar + card slack.</summary>
        private double ClosedMinWidth =>
            (RailCol.Width.Value + CalendarMinW + WindowChromeSlack()) * _appScale;

        /// <summary>Window minimum with the panel OPEN - the panel's width on top.</summary>
        private double OpenMinWidth => ClosedMinWidth + _sidebarWidth * _appScale;

        /// <summary>
        /// Start the window riding the panel open, if the panel would otherwise push the calendar
        /// under CalendarMinW. Grows by the panel's FULL width so the calendar keeps exactly the
        /// width it has; anything less and it would still shrink, just less visibly.
        ///
        /// Never touches a maximized or snapped window - resizing one un-maximizes it, which is a
        /// ruder thing to do than a narrow calendar. Clamped to the work area, so on a display too
        /// narrow for both the calendar simply gets what is left.
        /// </summary>
        private void GrowForSidebar()
        {
            _rideGrow = 0;
            if (WindowState != WindowState.Normal) return;
            if (ActualWidth >= OpenMinWidth) return;      // already room; the calendar can just shrink

            double grow = Math.Min(_sidebarWidth * _appScale,
                                   SystemParameters.WorkArea.Width - ActualWidth);
            if (grow <= 0) return;

            _rideBase = ActualWidth;
            _rideGrow = grow;
            StartWidthRide();

            // Keep it on screen: growing from a window already near the right edge would otherwise
            // hang the new width off the side of the work area.
            double right = SystemParameters.WorkArea.Right;
            double target = _rideBase + grow;
            if (Left + target > right)
            {
                double moved = Math.Max(SystemParameters.WorkArea.Left, right - target);
                _rideLeftShift = Left - moved;   // remembered so closing puts the window back
                Left = moved;
            }
        }

        /// <summary>How far left the window had to be nudged to fit the grown width on screen.
        /// Given back when the panel closes, or the window creeps left every open/close cycle.</summary>
        private double _rideLeftShift;

        /// <summary>Ride the panel closed, handing back exactly what was borrowed.</summary>
        private void ShrinkAfterSidebar()
        {
            // Drop the minimum FIRST, or the window cannot be narrowed back past the open minimum.
            MinWidth = ClosedMinWidth;

            if (_rideGrow <= 0) return;
            if (WindowState != WindowState.Normal) { _rideGrow = 0; return; }

            // If the user resized the window themselves while the panel was open, their width wins
            // and there is nothing to give back. 2px tolerance for fractional DPI rounding.
            if (Math.Abs(ActualWidth - (_rideBase + _rideGrow)) > 2) { _rideGrow = 0; return; }

            StartWidthRide();
        }

        private void StartWidthRide()
        {
            if (_riding) return;
            _riding = true;
            CompositionTarget.Rendering += RideWidth;
        }

        private void RideWidth(object? sender, EventArgs e)
        {
            // Same frame as the layout that produced ActualWidth, so the two cannot separate.
            Width = _rideBase + SidebarCol.ActualWidth * (_rideGrow / _sidebarWidth);
        }

        /// <summary>Detach the per-frame sync and settle on an exact value. Called when the slide
        /// finishes, in both directions.</summary>
        private void EndWidthRide(double sidebarWidth)
        {
            if (_riding)
            {
                CompositionTarget.Rendering -= RideWidth;
                _riding = false;
                Width = _rideBase + sidebarWidth * (_rideGrow / _sidebarWidth);

                // Put the window back where it was if growing had to nudge it off the right edge.
                // Without this it walks left a little on every open/close.
                if (!_sidebarOpen && _rideLeftShift != 0)
                {
                    Left += _rideLeftShift;
                    _rideLeftShift = 0;
                }
            }

            // Raise the floor only once the panel is fully out. Setting it while the window is
            // still narrow makes WPF snap the width up instantly, which is the jump this whole
            // mechanism exists to avoid.
            if (_sidebarOpen)
                MinWidth = Math.Min(OpenMinWidth, SystemParameters.WorkArea.Width);
            else
                _rideGrow = 0;
        }

        /// <summary>
        /// Width the window spends on things that are neither rail nor calendar nor panel: the
        /// 1px card border either side and the card's 8px right margin. Small, but leaving it out
        /// makes the calendar land a few px under its minimum, which defeats the point.
        /// </summary>
        private static double WindowChromeSlack() => 10;

        private void ClearSidebarError()
        {
            SidebarError.Text = "";
            SidebarError.Visibility = Visibility.Collapsed;
        }

        private void SlideSidebar(double to)
        {
            SidebarSplitter.IsEnabled = false;
            double from = SidebarCol.ActualWidth;
            var anim = new GridLengthAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(Anim.FadeMs)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Hand the final value back to the property and detach the clock. A held animation keeps
            // ownership of Width forever, so the next slide reads a stale ActualWidth and starts from
            // the wrong place - which looks like a jump partway through.
            anim.Completed += (_, _) =>
            {
                SidebarCol.BeginAnimation(ColumnDefinition.WidthProperty, null);
                SidebarCol.Width = new GridLength(to);
                SidebarContent.Width = _sidebarWidth;
                SidebarSplitter.IsEnabled = _sidebarOpen;
                // The window has been deriving its width from this column every frame; settle it
                // on the exact final value and detach. (See the width-ride block above.)
                EndWidthRide(to);
            };

            SidebarCol.BeginAnimation(ColumnDefinition.WidthProperty, anim);
        }
    }
}
