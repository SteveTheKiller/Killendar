using System.Windows;
using Killendar.Services;

namespace Killendar.Shell
{
    /// <summary>
    /// Killendar's half of the language wiring: the date settings menu that sits beside the
    /// language button, and the code-built calendar text that a live language switch rebuilds
    /// through the kit's RelocalizeAppUi hook (Shell/Language.cs).
    /// </summary>
    public partial class MainWindow
    {
        private Controls.DateSettingsMenu _dateSettingsMenu = null!;

        /// <summary>Call from the constructor after InitLanguageMenu.</summary>
        private void InitDateSettingsMenu()
        {
            _dateSettingsMenu = new Controls.DateSettingsMenu(
                DateSettingsContextMenu, DateSettingsButton, ReformatOpenEditor, ReformatOpenEditorTimes, RelocalizeDynamicUi);
            InitHolidayCalendar();   // HolidayCalendar.cs
        }

        private void DateSettingsButton_Click(object sender, RoutedEventArgs e) => _dateSettingsMenu.Open();

        /// <summary>Look up a localized string.</summary>
        private string Loc(string key) => LocaleManager.Loc(key);

        /// <summary>An open editor is showing dates in the old pattern, so reformat what is in the
        /// fields rather than leaving a mix of the two.</summary>
        private void ReformatOpenEditor(DateStyle style)
        {
            if (_sidebarOpen) _appointments.ChangeDateStyle(style);
            else DateFormatManager.Apply(style);
            RefreshDisplayedTimes();
        }

        private void ReformatOpenEditorTimes(TimeStyle style)
        {
            if (_sidebarOpen) _appointments.ChangeTimeStyle(style);
            else DateFormatManager.ApplyTime(style);
            RefreshDisplayedTimes();
        }

        /// <summary>Chips, the time gutter and agenda rows are built with the current date style
        /// and clock, so they are rebuilt when either changes.</summary>
        private void RefreshDisplayedTimes()
        {
            _calendar.Refresh();
            if (_agendaDay != null && AgendaScroll.Visibility == Visibility.Visible) BuildDayAgendaRows();
        }

        /// <summary>The calendar views, the time zone list, the shortcut tooltips and the open
        /// panel are built in code, so a language switch rebuilds them here.</summary>
        partial void RelocalizeAppUi()
        {
            // The views are rebuilt wholesale, which re-reads every string they use.
            _calendar.Refresh();

            // The time zone list names "this computer" in the language; rebuilt on next use.
            FieldZone.Items.Clear();
            if (ZoneRow.Visibility == Visibility.Visible) BuildZoneChoices();

            // Tooltips that carry a shortcut, including the panel button's, which tracks its state.
            ApplyShortcutTooltips();
            RefreshDensityTooltip();   // Density.cs builds it in code with the current step

            if (_agendaDay is System.DateTime day && AgendaScroll.Visibility == Visibility.Visible)
            {
                SidebarTitle.Text = day.ToString("D");
                BuildDayAgendaRows();
            }
            else
            {
                // The editor can retain an agenda day as its return target.
                _appointments.RefreshLocalizedText();
                BuildRepeatChips();
                BuildScopeChips();
                BuildReminderChoices();   // ReminderField.cs
            }
        }
    }
}
