using System.Windows;
using System.Windows.Controls;
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
// title, start time + title, then full time range + title. The day panel's own detail level is on
// the same button's right-click menu, since calendar spacing and appointment detail are separate choices.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        /// <summary>How much of each appointment the day panel shows. Set from the rail density
        /// button's right-click menu: one button, not a second copy beside the day heading.</summary>
        private int _sidebarDensity = CalendarChrome.MaxDensity;

        private static readonly string[] PanelDetailKeys =
            ["Str_Density_Panel0", "Str_Density_Panel1", "Str_Density_Panel2", "Str_Density_Panel3"];

        private void InitDensity()
        {
            CalendarChrome.Density =
                int.TryParse(Settings.Get("TimeGridDensity"), out int d) ? d : 0;   // the property clamps
            if (int.TryParse(Settings.Get("SidebarDensity"), out int sidebar))
                _sidebarDensity = Math.Max(0, Math.Min(CalendarChrome.MaxDensity, sidebar));
            // Right-click (or the menu key) offers the day panel's detail levels.
            // Opened here rather than by the context menu service, which would place it at the
            // pointer: it hangs off the pane like the other rail flyouts (DateSettingsMenu).
            DensityBtn.ContextMenu = new ContextMenu();
            DensityBtn.ContextMenuOpening += (_, e) =>
            {
                e.Handled = true;
                var menu = DensityBtn.ContextMenu;
                BuildPanelDetailMenu(menu);
                menu.IsOpen = true;
                Controls.Anim.FadeIn(menu);
            };
            RefreshDensityTooltip();
        }

        private void BuildPanelDetailMenu(ContextMenu menu)
        {
            menu.Items.Clear();
            menu.Items.Add(new MenuItem { Header = Loc("Str_Density_PanelHead"), IsEnabled = false });
            for (int level = CalendarChrome.MaxDensity; level >= 0; level--)
            {
                int chosen = level;
                var item = new MenuItem
                {
                    Header = Loc(PanelDetailKeys[level]),
                    IsCheckable = true,
                    IsChecked = level == _sidebarDensity,
                };
                item.Click += (_, _) => ApplySidebarDensity(chosen);
                menu.Items.Add(item);
            }
            Controls.FlyoutPlacement.Attach(menu, DensityBtn);
        }

        private void ApplySidebarDensity(int level)
        {
            if (level == _sidebarDensity) return;
            _sidebarDensity = level;
            Settings.Set("SidebarDensity", level.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (_agendaDay != null) BuildDayAgendaRows();
        }

        /// <summary>Click steps one tighter and wraps from the tightest back to the roomiest, as the
        /// density buttons in KillerNotes and KillerShell do, so the button alone reaches every step.
        /// Level 0 is the tightest here, so tighter is one level down.</summary>
        private void DensityBtn_Click(object sender, RoutedEventArgs e)
            => ApplyDensity((CalendarChrome.Density + CalendarChrome.MaxDensity) % (CalendarChrome.MaxDensity + 1));

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
            DensityBtn.ToolTip = (ViewHost?.Content is MonthView
                ? Loc("Str_TT_Density") + "  (" + (CalendarChrome.Density + 1) + "/4)"
                : Loc("Str_TT_Density") + "  (" + CalendarChrome.Subdivisions + ")")
                + "\n" + Loc("Str_TT_DensityPanel");
        }
    }
}
