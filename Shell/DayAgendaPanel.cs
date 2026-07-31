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
            // The rail's density button drives this list too (Steve, 2026-07-31): the same knob
            // that packs the hour grid tighter makes these rows say more, so the whole title and
            // the appointment's info are readable without hovering for the tooltip.
            //   0 - one trimmed line, as always
            //   1 - the title wraps instead of trimming
            //   2 - and the location shows under it
            //   3 - and the description and attendees too
            int detail = Views.CalendarChrome.Density;

            var row = new Grid();
            // The time column is SHARED across every row (AgendaListHost is the size scope), so
            // the widest label sets one column width for the whole list and every chip starts at
            // the same x - without it each row followed its own time label and the chips came
            // out three different widths (Steve, 2026-07-31).
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "AgendaTime" });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // chip
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // edit
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                            // chip row
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                            // details

            // A multi-day label ("7/15 11:30 PM - 7/16 1:30 AM") is the one that grows long
            // enough to starve the chip column - the column is Auto, so one patch window
            // squeezed every chip in the list (Steve, 2026-07-31). Stacked into two lines it
            // is never wider than an ordinary same-day time.
            string timeText = !ev.AllDay && ev.SpansMultipleDays
                ? ev.Start.ToString("M/d h:mm tt") + " -\n" + ev.End.ToString("M/d h:mm tt")
                : ev.TimeLabel;
            var time = Views.CalendarChrome.Text(timeText, "MutedTextBrush", 10.5, null, "Consolas");
            time.VerticalAlignment = detail >= 1 ? VerticalAlignment.Top : VerticalAlignment.Center;
            time.Margin = new Thickness(0, detail >= 1 ? 5 : 0, 8, 0);
            Grid.SetColumn(time, 0);
            row.Children.Add(time);

            // Chip click only marks the row - viewing, not editing (Steve, 2026-07-30).
            var chip = Views.CalendarChrome.Chip(ev,
                e => { _agendaHighlight = e.Id; BuildDayAgendaRows(); }, compact: false,
                wrap: detail >= 1);
            chip.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(chip, 1);
            row.Children.Add(chip);

            if (detail >= 2)
            {
                // Under the chip, aligned to it, dimmer than the title. Only lines that have
                // something to say are added - no empty-label placeholders.
                var info = new StackPanel { Margin = new Thickness(2, 2, 0, 2) };
                Grid.SetRow(info, 1);
                Grid.SetColumn(info, 1);

                if (!string.IsNullOrWhiteSpace(ev.Location))
                    info.Children.Add(DetailLine(ev.Location, "MutedTextBrush"));

                if (detail >= 3)
                {
                    if (!string.IsNullOrWhiteSpace(ev.Description))
                        info.Children.Add(DetailLine(ev.Description, "DimTextBrush"));
                    if (ev.Attendees.Count > 0)
                        info.Children.Add(DetailLine(string.Join(", ", ev.Attendees), "DimTextBrush"));
                }

                if (info.Children.Count > 0) row.Children.Add(info);
            }

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

        /// <summary>One wrapped info line under a chip - location, description or attendees.</summary>
        private static TextBlock DetailLine(string text, string brushKey)
        {
            var tb = new TextBlock
            {
                Text         = text,
                FontSize     = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 1, 0, 0)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return tb;
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
