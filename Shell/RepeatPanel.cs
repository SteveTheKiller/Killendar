using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;

// The REPEATS section of the appointment panel, and the "this date or the whole series" chips.
//
// The chips are built here rather than declared in XAML for two reasons. The weekday buttons take
// their letters AND their order from the culture, so a locale whose week starts on Monday gets it
// without seven more translated strings. And the pattern captions feed both the chips and the
// plain-English summary underneath, so the two can never end up describing different things.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        /// <summary>How a series stops. Three chips, one box: the box holds a count or a date
        /// depending on which chip is lit, because two boxes where only one can ever be used is a
        /// box that is permanently dead.</summary>
        private enum RepeatEndMode { Never, After, On }

        private RepeatFreq   _repeatFreq = RepeatFreq.None;
        private RepeatEndMode _repeatEnd = RepeatEndMode.Never;
        private readonly HashSet<DayOfWeek> _repeatDays = [];
        private int _repeatMonthWeek;
        private readonly List<DateTime> _extraDates = [];

        /// <summary>True when the panel is editing ONE date of a series and the user has chosen to
        /// apply the edit to all of them.</summary>
        private bool _editWholeSeries;
        private bool _editFollowing;
        private bool _followingOffered;

        /// <summary>Suppresses the chip handlers while the panel is being populated, so loading an
        /// appointment does not read back as the user having clicked things.</summary>
        private bool _loadingRepeat;

        // ── IAppointmentView: the repeat fields ───────────────────────────────────────────

        RepeatFreq IAppointmentView.FieldRepeat
        {
            get => _repeatFreq;
            set { _repeatFreq = value; BuildRepeatChips(); }
        }

        int IAppointmentView.FieldRepeatEveryN
        {
            get => int.TryParse(FieldRepeatInterval.Text.Trim(), out int n) && n > 0 ? n : 0;
            set => FieldRepeatInterval.Text = value.ToString(CultureInfo.CurrentCulture);
        }

        List<DateTime> IAppointmentView.FieldRepeatExtraDates
        {
            get => [.. _extraDates];
            set { _extraDates.Clear(); _extraDates.AddRange((value ?? []).Select(d => d.Date).Distinct().OrderBy(d => d)); BuildExtraChips(); }
        }

        /// <summary>The extra dates as chips; clicking one takes it off.</summary>
        private void BuildExtraChips()
        {
            if (RepeatExtraRow == null) return;
            RepeatExtraRow.Children.Clear();
            foreach (var day in _extraDates)
            {
                var chip = Chip(DateFormatManager.Format(day) + "  \u2715", true, () =>
                {
                    _extraDates.Remove(day);
                    BuildExtraChips();
                });
                System.Windows.Automation.AutomationProperties.SetName(chip, string.Format(Loc("Str_Rep_RemoveDate"), DateFormatManager.Format(day)));
                RepeatExtraRow.Children.Add(chip);
            }
        }

        private void InitRepeatExtras()
        {
            void Add()
            {
                if (!AppointmentEditor.TryParseDate(FieldRepeatExtra.Text, out var day))
                {
                    FieldRepeatExtra.ToolTip = Loc("Str_Hint_Date");
                    return;
                }
                if (!_extraDates.Contains(day.Date)) { _extraDates.Add(day.Date); _extraDates.Sort(); }
                FieldRepeatExtra.Text = "";
                BuildExtraChips();
            }
            RepeatExtraAdd.Click += (_, _) => Add();
            FieldRepeatExtra.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                e.Handled = true;
                Add();
            };
        }

        int IAppointmentView.FieldRepeatMonthWeek
        {
            get => _repeatMonthWeek;
            set { _repeatMonthWeek = value; BuildRepeatChips(); }
        }

        List<DayOfWeek> IAppointmentView.FieldRepeatDays
        {
            get => [.. _repeatDays.OrderBy(d => d)];
            set
            {
                _repeatDays.Clear();
                foreach (var d in value ?? []) _repeatDays.Add(d);
                BuildRepeatChips();
            }
        }

        int IAppointmentView.FieldRepeatCount
        {
            get => _repeatEnd == RepeatEndMode.After &&
                   int.TryParse(FieldRepeatEnd.Text.Trim(), out int n) && n > 0 ? n : 0;
            set
            {
                if (value <= 0) return;
                _repeatEnd = RepeatEndMode.After;
                FieldRepeatEnd.Text = value.ToString(CultureInfo.CurrentCulture);
                BuildRepeatChips();
            }
        }

        DateTime? IAppointmentView.FieldRepeatUntil
        {
            get => _repeatEnd == RepeatEndMode.On &&
                   AppointmentEditor.TryParseDate(FieldRepeatEnd.Text, out var d) ? d.Date : (DateTime?)null;
            set
            {
                if (value == null) return;
                _repeatEnd = RepeatEndMode.On;
                FieldRepeatEnd.Text = DateFormatManager.Format(value.Value);
                BuildRepeatChips();
            }
        }

        /// <summary>
        /// Resets the whole section to "does not repeat". Called before loading, so an appointment
        /// with no repeat cannot inherit the pattern of the one looked at before it.
        /// </summary>
        void IAppointmentView.ResetRepeat()
        {
            _loadingRepeat = true;
            _repeatFreq = RepeatFreq.None;
            _repeatEnd  = RepeatEndMode.Never;
            _repeatDays.Clear();
            _repeatMonthWeek = 0;
            _extraDates.Clear();
            FieldRepeatExtra.Text = "";
            BuildExtraChips();
            FieldRepeatInterval.Text = "1";
            FieldRepeatEnd.Text = "";
            _editWholeSeries = false;
            _editFollowing = false;
            _loadingRepeat = false;
            BuildRepeatChips();
        }

        /// <summary>Hides the pattern controls. An override edits ONE date and has no pattern of
        /// its own; showing an empty Repeats section there invites setting one.</summary>
        bool IAppointmentView.RepeatSectionVisible
        {
            set
            {
                RepeatFreqButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                if (!value)
                {
                    RepeatDetail.Visibility = Visibility.Collapsed;
                    RepeatIntervalRow.Visibility = Visibility.Collapsed;
                }
                else BuildRepeatChips();
            }
        }

        bool IAppointmentView.SeriesScopeVisible
        {
            set
            {
                SeriesScopeRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                if (value) BuildScopeChips();
            }
        }

        bool IAppointmentView.EditWholeSeries
        {
            get => _editWholeSeries;
            set { _editWholeSeries = value; if (value) _editFollowing = false; BuildScopeChips(); }
        }

        bool IAppointmentView.EditFollowing
        {
            get => _editFollowing && _followingOffered;
            set { _editFollowing = value; if (value) _editWholeSeries = false; BuildScopeChips(); }
        }

        bool IAppointmentView.FollowingOffered
        {
            set { _followingOffered = value; BuildScopeChips(); }
        }

        // ── Chip construction ─────────────────────────────────────────────────────────────

        private ToggleButton Chip(string caption, bool on, Action onClick, double minWidth = 0)
        {
            var b = new ToggleButton { Content = caption, IsChecked = on };
            if (minWidth > 0) b.MinWidth = minWidth;
            b.SetResourceReference(StyleProperty, "ChipToggle");
            b.Click += (_, _) => { if (!_loadingRepeat) onClick(); };
            return b;
        }

        private void RepeatFreqButton_Click(object sender, RoutedEventArgs e)
        {
            BuildRepeatMenu();
            RepeatFreqMenu.PlacementTarget = RepeatFreqButton;
            RepeatFreqMenu.Placement = PlacementMode.Bottom;
            RepeatFreqMenu.IsOpen = true;
        }

        /// <summary>One stable-width selector replaces the five wrapping frequency chips. The
        /// detailed controls below still expand for the selected pattern.</summary>
        private void BuildRepeatMenu()
        {
            if (RepeatFreqMenu == null) return;
            RepeatFreqMenu.Items.Clear();
            foreach (var (freq, key) in RepeatOptions)
            {
                var selected = freq == _repeatFreq;
                var item = new MenuItem
                {
                    Header = Loc(key),
                    IsChecked = selected,
                    StaysOpenOnClick = false,
                };
                item.Click += (_, _) =>
                {
                    if (_loadingRepeat) return;
                    _repeatFreq = freq;
                    BuildRepeatChips();
                };
                RepeatFreqMenu.Items.Add(item);
            }
        }

        /// <summary>The pattern row, the interval unit, the weekday row and the end row. Rebuilt
        /// whole on every change: there are fifteen small buttons, and rebuilding is how the
        /// captions stay correct after a language switch without a second refresh path.</summary>
        private void BuildRepeatChips()
        {
            if (RepeatFreqButton == null) return;   // called before InitializeComponent

            bool wasLoading = _loadingRepeat;
            _loadingRepeat = true;
            try
            {
                RepeatFreqButton.Content = Loc(RepeatOptions.First(o => o.Freq == _repeatFreq).Key);
                BuildRepeatMenu();

                bool repeats = _repeatFreq != RepeatFreq.None;
                RepeatDetail.Visibility = repeats ? Visibility.Visible : Visibility.Collapsed;
                RepeatIntervalRow.Visibility = repeats ? Visibility.Visible : Visibility.Collapsed;
                if (!repeats) return;

                RepeatUnitLabel.Text = Loc(UnitKey(_repeatFreq));
                // The box sits between "Every" and the unit with no label of its own, so a screen
                // reader would announce only the number. Name it with both, and update the unit.
                System.Windows.Automation.AutomationProperties.SetName(FieldRepeatInterval,
                    IntervalAccessibleName(_repeatFreq, Loc));

                // Weekdays belong to a weekly pattern only. "Every 2 months on a Tuesday" is an
                // ordinal rule, which Killendar does not draw and the .ics importer refuses.
                RepeatDaysRow.Visibility = _repeatFreq is RepeatFreq.Weekly or RepeatFreq.Monthly
                    ? Visibility.Visible : Visibility.Collapsed;
                if (_repeatFreq == RepeatFreq.Monthly) BuildMonthlyChoice();

                if (_repeatFreq == RepeatFreq.Weekly)
                {
                    RepeatDaysRow.Children.Clear();
                    var fmt   = CultureInfo.CurrentCulture.DateTimeFormat;
                    int first = (int)WeekStartManager.FirstDay;
                    for (int i = 0; i < 7; i++)
                    {
                        var day = (DayOfWeek)((first + i) % 7);
                        RepeatDaysRow.Children.Add(Chip(
                            fmt.ShortestDayNames[(int)day], _repeatDays.Contains(day),
                            () =>
                            {
                                if (!_repeatDays.Remove(day)) _repeatDays.Add(day);
                                BuildRepeatChips();
                            },
                            minWidth: 30));
                    }
                }

                RepeatEndRow.Children.Clear();
                foreach (var (mode, key) in EndOptions)
                    RepeatEndRow.Children.Add(Chip(Loc(key), _repeatEnd == mode,
                        () =>
                        {
                            // The one box means something different per mode, so a stale value
                            // from the other mode has to go rather than be reinterpreted.
                            if (_repeatEnd != mode) FieldRepeatEnd.Text = "";
                            _repeatEnd = mode;
                            BuildRepeatChips();
                        }));

                FieldRepeatEnd.Visibility = _repeatEnd == RepeatEndMode.Never
                    ? Visibility.Collapsed : Visibility.Visible;
                FieldRepeatEnd.ToolTip = _repeatEnd == RepeatEndMode.After
                    ? Loc("Str_Rep_HintCount")
                    : DateFormatManager.Hint;

                UpdateRepeatSummary();
            }
            finally { _loadingRepeat = wasLoading; }
        }

        /// <summary>
        /// Monthly: on the same date, or on that week's weekday of the start ("the third
        /// Wednesday"), or the last such weekday when the start is in the month's last week.
        /// Read from the start box, so the captions follow the date being typed.
        /// </summary>
        private void BuildMonthlyChoice()
        {
            RepeatDaysRow.Children.Clear();
            if (!AppointmentEditor.TryParseDate(FieldStartDate.Text, out var start)) return;
            int week = Recurrence.WeekOfMonth(start);
            string weekday = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(start.DayOfWeek);
            var choices = new List<(int Week, string Caption)> { (0, string.Format(Loc("Str_Rep_OnDay"), start.Day)) };
            if (week <= 4) choices.Add((week, string.Format(Loc("Str_Rep_OnNth" + week), weekday)));
            if (Recurrence.IsLastWeekdayOfMonth(start)) choices.Add((-1, string.Format(Loc("Str_Rep_OnLast"), weekday)));
            // A choice the new date no longer offers (moved out of the last week) falls back.
            if (!choices.Exists(c => c.Week == _repeatMonthWeek)) _repeatMonthWeek = Recurrence.FitMonthWeek(start, _repeatMonthWeek);
            foreach (var (value, caption) in choices)
                RepeatDaysRow.Children.Add(Chip(caption, _repeatMonthWeek == value,
                    () => { _repeatMonthWeek = value; BuildRepeatChips(); }));
        }

        private void BuildScopeChips()
        {
            if (SeriesScopeChips == null) return;

            bool wasLoading = _loadingRepeat;
            _loadingRepeat = true;
            try
            {
                SeriesScopeChips.Children.Clear();
                bool following = _editFollowing && _followingOffered;
                SeriesScopeChips.Children.Add(Chip(Loc("Str_Btn_ThisDate"), !_editWholeSeries && !following,
                    () => { _editWholeSeries = false; _editFollowing = false; BuildScopeChips(); }));
                if (_followingOffered)
                    SeriesScopeChips.Children.Add(Chip(Loc("Str_Btn_ThisAndFollowing"), following,
                        () => { _editWholeSeries = false; _editFollowing = true; BuildScopeChips(); }));
                SeriesScopeChips.Children.Add(Chip(Loc("Str_Btn_WholeSeries"), _editWholeSeries,
                    () => { _editWholeSeries = true; _editFollowing = false; BuildScopeChips(); }));

                // The PATTERN belongs to the series, so it is only editable once the edit is
                // aimed at the series. Offering it while "just this date" is lit would let you
                // set a pattern on a single occurrence, which is not a thing.
                if (SeriesScopeRow.Visibility == Visibility.Visible)
                {
                    bool pattern = _editWholeSeries || following;
                    RepeatFreqButton.Visibility = pattern ? Visibility.Visible : Visibility.Collapsed;
                    RepeatDetail.Visibility  = pattern && _repeatFreq != RepeatFreq.None
                                               ? Visibility.Visible : Visibility.Collapsed;
                    RepeatIntervalRow.Visibility = RepeatDetail.Visibility;
                }
            }
            finally { _loadingRepeat = wasLoading; }
        }

        /// <summary>
        /// True when a series has been told to end somehow but the box saying how is empty or
        /// unreadable. Save refuses rather than quietly treating it as "never ends" - a repeat
        /// that silently runs forever is exactly the surprise this feature must not create.
        /// </summary>
        bool IAppointmentView.RepeatEndIncomplete
        {
            get
            {
                if (_repeatFreq == RepeatFreq.None) return false;
                if (_repeatEnd == RepeatEndMode.After)
                    return !(int.TryParse(FieldRepeatEnd.Text.Trim(), out int n) && n > 0);
                if (_repeatEnd == RepeatEndMode.On)
                    return !AppointmentEditor.TryParseDate(FieldRepeatEnd.Text, out _);
                return false;
            }
        }

        /// <summary>
        /// Plain English underneath the controls, showing the next few dates the pattern actually
        /// produces. It is generated by the SAME engine that draws the calendar, so it cannot
        /// describe a schedule the calendar will not show - which is the point of having it.
        /// </summary>
        private void UpdateRepeatSummary()
        {
            if (RepeatSummary == null) return;

            if (_repeatFreq == RepeatFreq.None ||
                !AppointmentEditor.TryParseDate(FieldStartDate.Text, out var startDate))
            {
                RepeatSummary.Text = "";
                return;
            }

            var start = startDate.Date;
            if (AppointmentEditor.TryParseTime(FieldStartTime.Text, out var t)) start += t;

            var view = (IAppointmentView)this;
            var probe = new CalendarEvent
            {
                Start          = start,
                Repeat         = _repeatFreq,
                RepeatInterval = view.FieldRepeatEveryN,
                RepeatDays     = view.FieldRepeatDays,
                RepeatMonthWeek = _repeatFreq == RepeatFreq.Monthly ? _repeatMonthWeek : 0,
                RepeatCount    = view.FieldRepeatCount,
                RepeatUntil    = view.FieldRepeatUntil
            };

            var next = AppointmentEditor.RepeatPreview(probe);
            if (next.Length == 0)
            {
                RepeatSummary.Text = Loc("Str_Rep_SummaryNone");
                return;
            }

            var dates = next.Select(e => DateFormatManager.Format(e.Start));
            RepeatSummary.Text = string.Format(Loc("Str_Rep_SummaryNext"), string.Join(", ", dates));
        }

        private void RepeatInterval_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingRepeat) return;
            UpdateRepeatSummary();
        }

        // ── Tables ────────────────────────────────────────────────────────────────────────
        // One list each, so the chips and the summary can never disagree about what a pattern is
        // called or which patterns exist.

        private static readonly (RepeatFreq Freq, string Key)[] RepeatOptions =
        [
            (RepeatFreq.None,    "Str_Rep_Never"),
            (RepeatFreq.Daily,   "Str_Rep_Daily"),
            (RepeatFreq.Weekly,  "Str_Rep_Weekly"),
            (RepeatFreq.Monthly, "Str_Rep_Monthly"),
            (RepeatFreq.Yearly,  "Str_Rep_Yearly"),
        ];

        private static readonly (RepeatEndMode Mode, string Key)[] EndOptions =
        [
            (RepeatEndMode.Never, "Str_Rep_EndNever"),
            (RepeatEndMode.After, "Str_Rep_EndAfter"),
            (RepeatEndMode.On,    "Str_Rep_EndOn"),
        ];

        private static string UnitKey(RepeatFreq f)
        {
            return f switch
            {
                RepeatFreq.Daily => "Str_Rep_UnitDays",
                RepeatFreq.Weekly => "Str_Rep_UnitWeeks",
                RepeatFreq.Monthly => "Str_Rep_UnitMonths",
                _ => "Str_Rep_UnitYears",
            };
        }

        /// <summary>The interval box's accessible name: "Repeat interval (days)".</summary>
        internal static string IntervalAccessibleName(RepeatFreq f, Func<string, string> loc)
            => string.Format(loc("Str_Rep_IntervalName"), loc(UnitKey(f)));
    }
}
