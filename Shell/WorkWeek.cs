using System.Windows;
using Killendar.Views;

// ============================================================
// 5-DAY WORK WEEK (Steve, 2026-07-31)
//
// A rail toggle that drops Saturday and Sunday from Week view: five wider columns instead of
// seven cramped ones, which is most of what made the week grid unreadable. WeekView reads
// CalendarChrome.WorkWeek on every rebuild, so flipping the setting and refreshing is the whole
// job. On, the week always runs Monday to Friday whatever day the culture starts its week on.
//
// Persisted as "WorkWeek". Month, Day and Agenda ignore it.
// ============================================================
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void InitWorkWeek()
        {
            CalendarChrome.WorkWeek = Settings.Get("WorkWeek") == "1";
            // The toggle button lives in WeekView's own header corner (Steve, 2026-07-31 - it
            // was on the rail first, a whole window away from the columns it drops), so the view
            // invokes this hook rather than the shell owning a button.
            CalendarChrome.WorkWeekToggle = ToggleWorkWeek;
        }

        private void ToggleWorkWeek()
        {
            CalendarChrome.WorkWeek = !CalendarChrome.WorkWeek;
            Settings.Set("WorkWeek", CalendarChrome.WorkWeek ? "1" : "0");
            // The refresh rebuilds the header, which rebuilds the toggle with its new lit state.
            _calendar.Refresh();
            // StatusText directly, not SetStatus - same CS0103 reason as Density.cs.
            StatusText.Text = Loc(CalendarChrome.WorkWeek ? "Str_St_WorkWeekOn" : "Str_St_WorkWeekOff");
        }
    }
}
