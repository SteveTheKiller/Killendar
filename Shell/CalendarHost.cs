using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Killendar.Features;
using Killendar.Services;
using Killendar.Views;

// MainWindow's side of the calendar surface: it satisfies ICalendarHost, wires the toolbar, and
// composes the feature objects. The behavior lives in Features/Calendar/.
namespace Killendar.Shell
{
    public partial class MainWindow : ICalendarHost
    {
        private EventStore _store = null!;
        private CalendarController _calendar = null!;
        private IcsTransfer _ics = null!;

        // ---- composition root ----

        /// <summary>Called from the constructor once the XAML tree exists. Builds the store and the
        /// features and hands each the seams it needs. Deliberately the only place in the app that
        /// knows how the pieces fit together.</summary>
        private void InitCalendar()
        {
            _store = new EventStore();

            _appointments = new AppointmentEditor(this, _store);
            _security     = new SecurityController(this, _store);
            _calendar     = new CalendarController(this, _store, _appointments);
            _ics          = new IcsTransfer(this, _store, () => _calendar.Refresh());

            _store.Changed += () => _calendar.Refresh();
            // A color picker previewing a category repaints the calendar without writing, so it
            // cannot come through _store.Changed.
            Services.CategoryManager.Previewed += () => _calendar.Refresh();
            // Tag colors are theme-aware on the single-hue themes (CategoryManager.Displayed),
            // and chips carry literal brushes, so a theme switch must drop the brush cache and
            // repaint - in that order, or the repaint would rebuild from the stale cache.
            Services.ThemeManager.ThemeChanged += () =>
            {
                Services.CategoryManager.OnThemeChanged();
                _calendar.Refresh();
                // The sidebar's day list carries the same literal category brushes, so it must
                // rebuild too - _calendar.Refresh only repaints the views.
                if (_agendaDay != null) BuildDayAgendaRows();
            };

            _calendar.Initialize();         // builds the views, shows Month
            _security.RefreshLockState();   // so the title-bar lock is never blank before the open
        }

        /// <summary>
        /// Opens the active Killendar and repaints. Deliberately NOT called from the constructor: an
        /// encrypted Killendar prompts for its password, and a modal dialog needs an Owner that has
        /// already been shown, or it throws "Cannot set Owner property to a Window that has not been
        /// shown previously". Canceling the prompt also calls Close(), and a reentrant Close()
        /// inside Show() throws as well - which is why MainWindow dispatches this at Background
        /// priority from Loaded rather than calling it inline.
        /// </summary>
        private void OpenCalendarData()
        {
            _security.Open(exitOnCancel: true);
            _calendar.Refresh();

            // Precedence: a message the security controller parked (the fresh-Killendar escape
            // hatch), then whatever state the store came up in.
            StatusText.Text = _security.TakePendingStatus()
                              ?? _calendar.OpenStatus()
                              ?? StatusText.Text;
        }

        // ---- ICalendarHost ----

        string ICalendarHost.PeriodLabel { set => PeriodLabel.Text = value; }

        void ICalendarHost.ShowView(object view)
        {
            ViewHost.Content = view;
            // Month view owns a stepped silhouette: its spillover days and weekday axis are
            // deliberately transparent. The generic pane frame boxed those hollow regions back
            // in, while Week/Day/Agenda still need the normal full-rectangle outline.
            ContentPaneOutline.Visibility = view is MonthView
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        void ICalendarHost.StepDensity(int direction) => DensityStepped(direction);   // Density.cs

        /// <summary>
        /// Calendar view selection follows the family colors, with a brown tile in Sepulchre.
        /// </summary>
        void ICalendarHost.HighlightTab(string which)
        {
            foreach (var (btn, tag) in new[]
                     {
                         (TabMonth, "Month"), (TabWeek, "Week"), (TabDay, "Day"), (TabAgenda, "Agenda")
                     })
            {
                bool on = tag == which;
                btn.SetResourceReference(ForegroundProperty, on ? "SelectionFg" : "TextBrush");
                btn.CommandParameter = on ? "selected" : null;
                if (on) btn.SetResourceReference(BackgroundProperty, "CalendarViewSelectedBrush");
                else    btn.Background = System.Windows.Media.Brushes.Transparent;
            }

            // Month and the time grids own their actual calendar surfaces. This leaves Month's
            // weekday axis and Week's day labels directly on the window background instead of
            // trapping them in a second full-size card. Agenda still uses the ordinary card.
            bool ownsSurface = which is "Month" or "Week" or "Day";
            if (ownsSurface)
            {
                ContentPane.Background = Brushes.Transparent;
                ContentPane.BorderThickness = new Thickness(0);
                ContentPaneGrain.Visibility = Visibility.Collapsed;
            }
            else
            {
                ContentPane.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
                ContentPane.BorderThickness = new Thickness(1);
                ContentPaneGrain.Visibility = Visibility.Visible;
            }

            if (ContentPane.Effect is DropShadowEffect shadow)
                shadow.Opacity = ownsSurface ? 0 :
                    TryFindResource("PaneShadowOpacity") is double opacity ? opacity : 0.6;

            var bevelVisibility = ownsSurface ? Visibility.Collapsed : Visibility.Visible;
            ContentPaneBevel1.Visibility = bevelVisibility;
            ContentPaneBevel2.Visibility = bevelVisibility;
            ContentPaneBevel3.Visibility = bevelVisibility;
            ContentPaneBevel4.Visibility = bevelVisibility;
            RefreshDensityTooltip();
        }

        void ICalendarHost.ShowFineMonthNavigation(bool visible)
        {
            var state = visible ? Visibility.Visible : Visibility.Collapsed;
            PrevWeekBtn.Visibility = state;
            NextWeekBtn.Visibility = state;
            PrevBtn.Content = visible ? "«" : ((char)0xE76B).ToString();
            NextBtn.Content = visible ? "»" : ((char)0xE76C).ToString();
            PrevBtn.FontFamily = visible ? SystemFonts.MessageFontFamily : new FontFamily("Segoe MDL2 Assets");
            NextBtn.FontFamily = visible ? SystemFonts.MessageFontFamily : new FontFamily("Segoe MDL2 Assets");
            PrevBtn.FontSize = NextBtn.FontSize = visible ? 18 : 10;
            PrevBtn.SetResourceReference(ToolTipProperty, visible ? "Str_TT_PrevRange" : "Str_TT_Prev");
            NextBtn.SetResourceReference(ToolTipProperty, visible ? "Str_TT_NextRange" : "Str_TT_Next");
        }

        // ---- toolbar ----

        private void ViewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag) _calendar.SelectView(tag);
        }

        private void MonthModeMenu_Opened(object sender, RoutedEventArgs e)
        {
            bool rolling = _calendar.MonthRollingMode;
            MonthCalendarModeItem.IsChecked = !rolling;
            MonthRollingModeItem.IsChecked = rolling;
        }

        private void MonthCalendarMode_Click(object sender, RoutedEventArgs e)
            => _calendar.SetMonthRollingMode(false);

        private void MonthRollingMode_Click(object sender, RoutedEventArgs e)
            => _calendar.SetMonthRollingMode(true);

        private void PrevBtn_Click(object sender, RoutedEventArgs e) => _calendar.Move(-1);

        private void PrevWeekBtn_Click(object sender, RoutedEventArgs e) => _calendar.MoveOneWeek(-1);

        private void NextBtn_Click(object sender, RoutedEventArgs e) => _calendar.Move(1);

        private void NextWeekBtn_Click(object sender, RoutedEventArgs e) => _calendar.MoveOneWeek(1);

        private void TodayBtn_Click(object sender, RoutedEventArgs e) => _calendar.GoToday();

        private void NewEventBtn_Click(object sender, RoutedEventArgs e) => _calendar.NewAtAnchor();

        private void ImportBtn_Click(object sender, RoutedEventArgs e) => _ics.Import();

        private void ExportBtn_Click(object sender, RoutedEventArgs e) => _ics.Export();
    }
}
