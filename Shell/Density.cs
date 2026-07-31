using System.Windows;
using System.Windows.Input;
using Killendar.Views;

// ============================================================
// TIME-GRID DENSITY
//
// How tightly Week and Day pack an hour. ONE scale drives both the hour height and the number of
// gridlines inside an hour, so a denser grid is also a taller one (Steve, 2026-07-30) - the two
// cannot drift apart, and there is only one thing to set.
//
// Four steps, 0 to 3: hour lines only, half hours, thirds, quarter hours. The click target for a
// new appointment follows the visible subdivision (CalendarChrome.SnapMinutes), so the grid never
// draws a line you cannot land on and never lands you somewhere it did not draw.
//
// Two ways in, both wired (Steve picked both):
//   * the rail button - click cycles, wheel over it steps. KillerNotes' Density button behaves
//     exactly this way, down to the E8A1 glyph.
//   * Ctrl+wheel anywhere over the grid. TimeGridView raises DensityStepped; plain wheel still
//     scrolls the day.
//
// Persisted as "TimeGridDensity". Month and the Agenda VIEW ignore it - neither has an hour
// grid. The sidebar's day agenda follows it too (Steve, 2026-07-31), reading it as detail per
// row rather than lines per hour: step 0 is one trimmed line, then the title wraps, then the
// location shows, then description and attendees - so at the top step everything the hover
// tooltip says is on the row itself.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void InitDensity()
        {
            CalendarChrome.Density =
                int.TryParse(Settings.Get("TimeGridDensity"), out int d) ? d : 0;   // the property clamps
            RefreshDensityTooltip();
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

            // The sidebar's day agenda reads density as detail per row; rebuild it if showing.
            if (_agendaDay != null) BuildDayAgendaRows();
            // StatusText directly, NOT SetStatus: that is an EXPLICIT IShellServices implementation
            // and so is not callable unqualified from inside the class. Language.cs writes the
            // status the same way; Install.cs casts instead. (CS0103.)
            StatusText.Text = string.Format(Loc("Str_St_Density"), CalendarChrome.Subdivisions);
        }

        /// <summary>The tooltip carries the current step, so the button says what it will do.</summary>
        private void RefreshDensityTooltip()
        {
            if (DensityBtn == null) return;
            DensityBtn.ToolTip = Loc("Str_TT_Density") + "  (" + CalendarChrome.Subdivisions + ")";
        }
    }
}
