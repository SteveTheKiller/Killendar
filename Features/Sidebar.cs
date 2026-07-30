using System;
using System.Windows;
using System.Windows.Media.Animation;

// The Killendar - appointment sidebar slide. Partial of MainWindow.
//
// The panel is never hidden with Visibility: the SidebarCol column width is animated between 0
// and SidebarW instead, so the calendar reflows with the slide rather than snapping after it.
// The rail beside it is permanent, which is the point of a rail - one fixed place to reach for.
namespace Killendar
{
    public partial class MainWindow
    {
        /// <summary>Open width of the appointment panel. Matches the fixed content width in XAML.</summary>
        private const double SidebarW = 330;

        // Segoe MDL2 chevrons, written as escapes so this file stays pure ASCII on disk (the same
        // encoding trap that made KillerPDF's release.ps1 PS7-only). The panel is on the LEFT, so
        // closed shows E76C (points right, the direction the calendar gets pushed) and open shows
        // E76B (points left, back into the edge it folds away to).
        private const string ChevronOpen  = "\uE76C";
        private const string ChevronClose = "\uE76B";

        private bool _sidebarOpen;

        private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_sidebarOpen) CloseSidebar();
            else              OpenSidebarPanel();
        }

        /// <summary>Slide the panel open. Safe to call when already open - it just refreshes the chrome.</summary>
        private void OpenSidebarPanel()
        {
            // Only animate on a real state change. Re-running the slide every time an appointment
            // is clicked made opening a second one while the panel was already open re-play the
            // whole thing. There is deliberately NO opacity fade here either: fading the panel
            // renders the subtree to an intermediate surface, which drops the text off ClearType
            // for the duration and snaps it back at the end. The width slide alone is the effect.
            if (!_sidebarOpen) SlideSidebar(SidebarW);

            _sidebarOpen = true;
            SidebarToggleBtn.Content = ChevronClose;
            SidebarToggleBtn.Tag = "on";                        // lights the rail icon in the accent
            SidebarToggleBtn.ToolTip = Loc("Str_TT_PanelHide");
        }

        /// <summary>Slide the panel shut and drop whatever was being edited.</summary>
        private void CloseSidebar()
        {
            if (_sidebarOpen) SlideSidebar(0);
            _sidebarOpen = false;
            SidebarToggleBtn.Content = ChevronOpen;
            SidebarToggleBtn.Tag = null;
            SidebarToggleBtn.ToolTip = Loc("Str_TT_PanelShow");

            _editing = null;                                    // Appointments.cs
            ClearSidebarError();
        }

        private void SlideSidebar(double to)
        {
            double from = SidebarCol.ActualWidth;
            var anim = new GridLengthAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(Anim.FadeMs)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Hand the final value back to the property and detach the clock. A held animation
            // keeps ownership of Width forever, so the next slide reads a stale ActualWidth and
            // starts from the wrong place - which looks like a jump partway through.
            anim.Completed += (_, _) =>
            {
                SidebarCol.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, null);
                SidebarCol.Width = new GridLength(to);
            };

            SidebarCol.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, anim);
        }
    }
}
