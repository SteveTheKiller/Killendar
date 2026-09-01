using System.Windows;
using System.Windows.Input;
using Killendar.Views;

// ============================================================
// TIME-GRID DENSITY
//
// How tightly Week and Day pack an hour. ONE scale drives both the hour height and the number of
// gridlines inside an hour, so a denser grid is also a taller one (2026-07-30) - the two
// cannot drift apart, and there is only one thing to set.
//
// Four steps, 0 to 3: hour lines only, half hours, thirds, quarter hours. The click target for a
// new appointment follows the visible subdivision (CalendarChrome.SnapMinutes), so the grid never
// draws a line you cannot land on and never lands you somewhere it did not draw.
//
// Two ways in, both wired:
//   * the rail button - click cycles, wheel over it steps. KillerNotes' Density button behaves
//     exactly this way, down to the E8A1 glyph.
//   * Ctrl+wheel anywhere over the grid. TimeGridView raises DensityStepped; plain wheel still
//     scrolls the day.
//
// Persisted as "TimeGridDensity". Month reads the same four steps as event detail: colored stripe,
// title, start time + title, then full time range + title. The sidebar has its own persisted detail
// level because calendar spacing and appointment detail are separate choices.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private int _sidebarDensity = CalendarChrome.MaxDensity;

        private void InitDensity()
        {
            CalendarChrome.Density =
                int.TryParse(Settings.Get("TimeGridDensity"), out int d) ? d : 0;   // the property clamps
            if (int.TryParse(Settings.Get("SidebarDensity"), out int sidebar))
                _sidebarDensity = ClampDensity(sidebar);
            RefreshDensityTooltip();
            RefreshSidebarDensityTooltip();
        }

        /// <summary>Click cycles forward and wraps, so the button alone can reach every step.</summary>
        private void DensityBtn_Click(object sender, RoutedEventArgs e)
            => ApplyDensity((CalendarChrome.Density + 1) % (CalendarChrome.MaxDensity + 1));

        /// <summary>Wheel over the button steps without wrapping - a wheel has a direction, so
        /// rolling up should not drop you back to the loosest grid.</summary>
        private void DensityBtn_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            ApplyDensity(CalendarChrome.Density + (e.Delta > 0 ? +1 : -1));
        }

        /// <summary>Ctrl+wheel over the grid itself. Raised by TimeGridView.</summary>
        private void DensityStepped(int direction) => ApplyDensity(CalendarChrome.Density + direction);

        private void ApplyDensity(int level)
        {
            int was = CalendarChrome.Density;
            CalendarChrome.Density = level;             // clamps
            if (CalendarChrome.Density == was) return;  // at an end stop - no repaint, no status churn

            Settings.Set("TimeGridDensity", CalendarChrome.Density.ToString());
            RefreshDensityTooltip();

            // Every hour position on screen just moved, so this one genuinely needs a full rebuild -
            // unlike the selection marker, which repaints two cells.
            _calendar.Refresh();

            // StatusText directly, NOT SetStatus: that is an EXPLICIT IShellServices implementation
            // and so is not callable unqualified from inside the class. Language.cs writes the
            // status the same way; Install.cs casts instead. (CS0103.)
            StatusText.Text = ViewHost.Content is MonthView
                ? Loc("Str_TT_Density") + "  (" + (CalendarChrome.Density + 1) + "/4)"
                : string.Format(Loc("Str_St_Density"), CalendarChrome.Subdivisions);
        }

        /// <summary>The tooltip carries the current step, so the button says what it will do.</summary>
        private void RefreshDensityTooltip()
        {
            if (DensityBtn == null) return;
            DensityBtn.ToolTip = ViewHost?.Content is MonthView
                ? Loc("Str_TT_Density") + "  (" + (CalendarChrome.Density + 1) + "/4)"
                : Loc("Str_TT_Density") + "  (" + CalendarChrome.Subdivisions + ")";
        }

        private void SidebarDensityBtn_Click(object sender, RoutedEventArgs e)
            => ApplySidebarDensity((_sidebarDensity + 1) % (CalendarChrome.MaxDensity + 1));

        private void SidebarDensityBtn_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            ApplySidebarDensity(_sidebarDensity + (e.Delta > 0 ? 1 : -1));
        }

        private void ApplySidebarDensity(int level)
        {
            int next = ClampDensity(level);
            if (next == _sidebarDensity) return;
            _sidebarDensity = next;
            Settings.Set("SidebarDensity", _sidebarDensity.ToString());
            RefreshSidebarDensityTooltip();
            if (_agendaDay != null) BuildDayAgendaRows();
        }

        private void RefreshSidebarDensityTooltip()
        {
            SidebarDensityBtn?.ToolTip = Loc("Str_TT_Density") + "  (" + (_sidebarDensity + 1) + "/4)";
        }

        private static int ClampDensity(int level)
            => level < 0 ? 0 : level > CalendarChrome.MaxDensity ? CalendarChrome.MaxDensity : level;
    }
}
