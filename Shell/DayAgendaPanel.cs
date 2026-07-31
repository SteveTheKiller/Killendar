using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Killendar.Features;
using Killendar.Models;

// The sidebar's DAY AGENDA mode (Steve, 2026-07-30): clicking a day or an appointment shows the
// day's appointments here FIRST - viewing is the default, and the edit form sits behind each
// row's Edit button. The editor's Save/Cancel/Delete return here rather than closing the panel;
// only the rail chevron and Esc close outright (SidebarSlide.cs / Shortcuts.cs).
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        /// <summary>Day the agenda panel is showing, and the return target after an edit.
        /// Null when the sidebar is closed or was opened straight into the editor ("+ New").</summary>
        private DateTime? _agendaDay;

        /// <summary>The appointment that was clicked, marked in the list with the family
        /// selection fill. Survives an edit round-trip so the row stays marked after Save.</summary>
        private Guid? _agendaHighlight;

        void ICalendarHost.ShowDayAgenda(DateTime day, CalendarEvent? highlight)
            => ShowDayAgenda(day, highlight?.Id);

        private void ShowDayAgenda(DateTime day, Guid? highlight)
        {
            _agendaDay = day;
            _agendaHighlight = highlight;

            _appointments.Clear();          // the panel is not editing anything in this mode
            ClearSidebarError();
            SidebarMode(agenda: true);
            SidebarTitle.Text = day.ToString("D");   // culture long date - the panel's heading
            BuildDayAgendaRows();
            OpenSidebarPanel();

            // The marker says which day the panel is talking about, exactly as the editor's
            // start boxes do while composing.
            _calendar.SetSelection(day, null);
        }

        /// <summary>Editor and agenda share the sidebar's two content rows, visibility-swapped.
        /// The editor's OpenPanel switches back to editor mode itself, so "+ New" from the rail
        /// never shows a stale agenda.</summary>
        private void SidebarMode(bool agenda)
        {
            EditorScroll.Visibility  = agenda ? Visibility.Collapsed : Visibility.Visible;
            EditorActions.Visibility = agenda ? Visibility.Collapsed : Visibility.Visible;
            AgendaScroll.Visibility  = agenda ? Visibility.Visible : Visibility.Collapsed;
            AgendaActions.Visibility = agenda ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildDayAgendaRows()
        {
            AgendaListHost.Children.Clear();
            if (_agendaDay == null) return;

            var events = _store.GetOnDay(_agendaDay.Value)
                               .OrderBy(ev => !ev.AllDay)      // all-day first, as the views draw them
                               .ThenBy(ev => ev.Start)
                               .ToList();

            if (events.Count == 0)
            {
                var none = new TextBlock
                {
                    Text         = Loc("Str_Side_NoAppointments"),
                    FontFamily   = new FontFamily("Consolas"),
                    FontSize     = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 8, 0, 0)
                };
                none.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
                AgendaListHost.Children.Add(none);
                return;
            }

            foreach (var ev in events)
                AgendaListHost.Children.Add(BuildDayAgendaRow(ev));
        }

        private FrameworkElement BuildDayAgendaRow(CalendarEvent ev)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // time
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // chip
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // edit

            var time = Views.CalendarChrome.Text(ev.TimeLabel, "MutedTextBrush", 10.5, null, "Consolas");
            time.VerticalAlignment = VerticalAlignment.Center;
            time.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(time, 0);
            row.Children.Add(time);

            // Chip click only marks the row - viewing, not editing (Steve, 2026-07-30).
            var chip = Views.CalendarChrome.Chip(ev,
                e => { _agendaHighlight = e.Id; BuildDayAgendaRows(); }, compact: false);
            chip.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(chip, 1);
            row.Children.Add(chip);

            // Glyph with a tooltip, not a text button (Steve, 2026-07-30). E70F is the pencil,
            // shipped and proven in KillerNotes' and KillerShell's rails; codepoint, never a
            // literal PUA char (family rule).
            var edit = new Button
            {
                Content           = ((char)0xE70F).ToString(),
                FontFamily        = new FontFamily("Segoe MDL2 Assets"),
                FontSize          = 13,
                Margin            = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip           = Loc("Str_Btn_Edit")
            };
            edit.SetResourceReference(StyleProperty, "RailButton");
            // Load switches the panel to editor mode itself (IAppointmentView.OpenPanel);
            // _agendaDay stays set, so Save/Cancel land back on this day's list.
            edit.Click += (_, _) => { _agendaHighlight = ev.Id; _appointments.Load(ev); };
            Grid.SetColumn(edit, 2);
            row.Children.Add(edit);

            // The marked row wears the family SelectionBg fill, on a wrapper so the fill gets
            // corners without restyling the chip.
            var wrap = new Border
            {
                Child        = row,
                CornerRadius = new CornerRadius(3),
                Padding      = new Thickness(3, 2, 3, 2),
                Margin       = new Thickness(0, 1, 0, 1)
            };
            if (_agendaHighlight == ev.Id)
                wrap.SetResourceReference(Border.BackgroundProperty, "SelectionBg");
            return wrap;
        }

        /// <summary>The agenda's action row: compose on the day being shown.</summary>
        private void AgendaNew_Click(object sender, RoutedEventArgs e)
        {
            if (_agendaDay == null) return;
            _appointments.NewAt(_agendaDay.Value.AddHours(9));   // OpenPanel swaps the mode
        }

        /// <summary>
        /// The editor finished - Save, Delete or Cancel. With a day agenda pending, return to it
        /// REBUILT, so the change just made is visible; otherwise the sidebar closes as before.
        /// </summary>
        private void CloseEditorPanel()
        {
            if (_agendaDay is DateTime day) ShowDayAgenda(day, _agendaHighlight);
            else CloseSidebar();
        }

        /// <summary>
        /// Full close (rail chevron, Esc) forgets the agenda. Called from CloseSidebar.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT call SidebarMode. CloseSidebar runs while the panel's column is
        /// mid-animation, and flipping the four content rows' Visibility there re-measures the
        /// whole panel on the frames the slide is trying to use - which is exactly the jerky close
        /// Steve caught on 2026-07-30. Nothing needs the swap here anyway: whichever mode opens
        /// next sets it (ShowDayAgenda -> agenda, IAppointmentView.OpenPanel -> editor), and the
        /// panel is invisible in between.
        /// </remarks>
        private void ResetDayAgenda()
        {
            _agendaDay = null;
            _agendaHighlight = null;
        }
    }
}
