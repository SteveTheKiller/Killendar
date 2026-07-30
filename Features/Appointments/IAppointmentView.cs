using System.Windows;

namespace Killendar.Features
{
    /// <summary>Which field to put the caret in when validation rejects an entry.</summary>
    internal enum AppointmentField { Title, StartDate, StartTime, EndDate, EndTime }

    /// <summary>
    /// The appointment panel as AppointmentEditor sees it: text in and out, plus a handful of
    /// commands. Everything is a string or a bool rather than a TextBox, so the editor's parsing
    /// and validation can be exercised without a window - which is the whole point, because that
    /// is where the logic worth testing lives.
    /// </summary>
    internal interface IAppointmentView
    {
        string FieldTitle       { get; set; }
        string FieldLocation    { get; set; }
        string FieldDescription { get; set; }
        string FieldAttendees   { get; set; }
        string FieldStartDate   { get; set; }
        string FieldStartTime   { get; set; }
        string FieldEndDate     { get; set; }
        string FieldEndTime     { get; set; }

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

        void Focus(AppointmentField field, bool selectAll = false);

        /// <summary>Slides the panel open, or shut and forgets what was being edited.</summary>
        void OpenPanel();
        void ClosePanel();

        void SetStatus(string text);
        string Loc(string key);

        /// <summary>Owner for the delete confirm.</summary>
        Window Window { get; }
    }
}
