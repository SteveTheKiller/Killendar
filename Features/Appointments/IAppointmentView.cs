namespace Killendar.Features
{
    /// <summary>Which field to put the caret in when validation rejects an entry.</summary>
    internal enum AppointmentField { Title, StartDate, StartTime, EndDate, EndTime, RepeatInterval }

    /// <summary>What to do with unsaved appointment input that is about to be replaced.</summary>
    internal enum DraftChoice { KeepEditing, Save, Discard }

    /// <summary>
    /// The appointment panel as AppointmentEditor sees it: text in and out, plus a handful of
    /// commands. Everything is a string or a bool rather than a TextBox, so the editor's parsing and
    /// validation can be exercised without a window - which is the point, because that is where the
    /// logic worth testing lives.
    /// </summary>
    internal interface IAppointmentView : IShellServices
    {
        string FieldTitle       { get; set; }
        string FieldLocation    { get; set; }
        string FieldDescription { get; set; }
        string FieldAttendees   { get; set; }

        /// <summary>Assigned categories as the comma-separated names EventStore stores. A string
        /// like every other field, even though the shell renders it as toggle chips, so the
        /// editor's logic stays exercisable without a window.</summary>
        string FieldCategories  { get; set; }

        /// <summary>Minutes before the start to remind, or null for none.</summary>
        int? FieldReminder { get; set; }

        /// <summary>Show as free (TRANSP:TRANSPARENT) rather than busy.</summary>
        bool FieldShowAsFree { get; set; }

        /// <summary>The read-only meeting details of an imported invitation, or null to hide them.</summary>
        void ShowMeeting(Services.MeetingInfo? meeting);

        /// <summary>The time zone the times are typed in: a zone id, or "" for this computer's.</summary>
        string FieldZoneId { get; set; }

        /// <summary>Whether the time zone choice is shown, and whether it can be changed (a
        /// series keeps the zone it was made in).</summary>
        void ShowZone(bool visible, bool editable);
        string FieldStartDate   { get; set; }
        string FieldStartTime   { get; set; }
        string FieldEndDate     { get; set; }
        string FieldEndTime     { get; set; }

        // ── Repeats ──────────────────────────────────────────────────────────────────────

        /// <summary>The repeat pattern, or None.</summary>
        Models.RepeatFreq FieldRepeat { get; set; }

        /// <summary>The "every N" number, or zero while the interval input is invalid.</summary>
        int FieldRepeatEveryN { get; set; }

        /// <summary>Monthly only: 0 for the same date, 1 to 4 or -1 for that week's weekday.</summary>
        int FieldRepeatMonthWeek { get; set; }

        /// <summary>Weekly only: which weekdays are ticked. Empty means the day it starts on.</summary>
        System.Collections.Generic.List<System.DayOfWeek> FieldRepeatDays { get; set; }

        /// <summary>Stop after this many occurrences, or 0 when that is not how it ends.</summary>
        int FieldRepeatCount { get; set; }

        /// <summary>Stop on this date, or null when that is not how it ends.</summary>
        System.DateTime? FieldRepeatUntil { get; set; }

        /// <summary>A date on a day off: 0 stays, 1 moves to the next workday, -1 to the one before.</summary>
        int FieldRepeatShift { get; set; }

        /// <summary>Dates the series also falls on, outside its pattern.</summary>
        System.Collections.Generic.List<System.DateTime> FieldRepeatExtraDates { get; set; }

        /// <summary>Clears the whole section back to "does not repeat", so one appointment cannot
        /// inherit the pattern of the last one loaded.</summary>
        void ResetRepeat();

        /// <summary>Whether the pattern controls are offered at all. Hidden while editing a single
        /// date of a series: that date has no pattern of its own, the series does.</summary>
        bool RepeatSectionVisible { set; }

        /// <summary>Whether the "this date / the whole series" chips are shown.</summary>
        bool SeriesScopeVisible { set; }

        /// <summary>Which of those chips is lit. True means the edit applies to every date.</summary>
        bool EditWholeSeries { get; set; }

        /// <summary>The third chip: the edit applies to this date and every later one. Never lit
        /// together with <see cref="EditWholeSeries"/>.</summary>
        bool EditFollowing { get; set; }

        /// <summary>Whether the "this and following" chip is offered (the series can be split).</summary>
        bool FollowingOffered { set; }

        /// <summary>True when the series is set to end but the box saying when is empty or
        /// unreadable, so Save can refuse instead of silently repeating forever.</summary>
        bool RepeatEndIncomplete { get; }

        /// <summary>Panel heading: composing versus editing.</summary>
        string Heading { set; }

        /// <summary>Whether the Delete button is offered (only when editing an existing one).</summary>
        bool CanDelete { set; }

        /// <summary>Tooltip for the date boxes, showing the pattern actually in force.</summary>
        string DateHint { set; }

        /// <summary>All-day caption, and whether the time boxes are shown at all.</summary>
        void SetAllDay(string caption, bool timesVisible);

        void ShowError(string message);
        void ClearError();

        /// <summary>Asks before typed changes are replaced by something else: save them, discard
        /// them, or keep editing (also Esc and the close button).</summary>
        DraftChoice AskAboutDraft();

        void Focus(AppointmentField field, bool selectAll = false);

        /// <summary>Slides the panel open, or shut and forgets what was being edited.</summary>
        void OpenPanel();
        void ClosePanel();

        /// <summary>
        /// Marks the day and half hour the panel is talking about, or clears the mark with nulls.
        /// The editor does not know about the calendar, so the shell forwards it. A null time means
        /// "no particular slot" - all-day, or a time box mid-edit - and leaves the day marked.
        /// </summary>
        void HighlightSelection(System.DateTime? day, System.TimeSpan? timeOfDay);
    }
}
