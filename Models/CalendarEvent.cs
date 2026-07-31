using System;
using System.Collections.Generic;

namespace Killendar.Models
{
    /// <summary>How often an appointment repeats. Stored as the integer, so the numbers are
    /// permanent - append to the end, never renumber.</summary>
    public enum RepeatFreq
    {
        None    = 0,
        Daily   = 1,
        Weekly  = 2,
        Monthly = 3,   // same day number each month; a 31st is skipped in shorter months
        Yearly  = 4
    }

    /// <summary>
    /// One appointment. Field names are load-bearing: IcsService maps them straight onto
    /// RFC-5545 VEVENT properties, and EventStore serialises them as-is, so renaming a
    /// property silently breaks both ICS round-tripping and every previously saved file.
    ///
    /// REPEATS. There are three kinds of row in the store, and every repeat question comes back
    /// to telling them apart:
    ///   - a PLAIN appointment: Repeat None, SeriesId null. What Killendar had before repeats.
    ///   - a SERIES MASTER: Repeat set, SeriesId null. ONE row that stands for every occurrence.
    ///     The occurrences are worked out when a view asks for a date range (Services.Recurrence),
    ///     never written out as rows - so changing the series is one edit, not five hundred.
    ///   - an OVERRIDE: SeriesId set to its master's Id, OccurrenceStart naming which occurrence
    ///     it replaces. This is what "just this one" produces when the user edits a single date.
    /// A date the user deleted singly is not a row at all: it goes in the master's SkipDates.
    /// </summary>
    public class CalendarEvent
    {
        public Guid         Id          { get; set; } = Guid.NewGuid();
        public string       Title       { get; set; } = "";
        public DateTime     Start       { get; set; }
        public DateTime     End         { get; set; }
        public bool         AllDay      { get; set; }
        public string       Location    { get; set; } = "";
        public string       Description { get; set; } = "";
        public List<string> Attendees   { get; set; } = [];
        public DateTime     Created     { get; set; } = DateTime.UtcNow;
        public DateTime     Modified    { get; set; } = DateTime.UtcNow;

        /// <summary>Assigned categories as a comma-separated list of names ("Work, Client").
        /// The definitions themselves (name + color) live in the categories table of the open
        /// Killendar, so they travel inside the .kcal; this column is only the assignment.
        /// Same shape as KillerNotes' notes.tags, for the same reason: no join table to keep
        /// in step, and one string is trivial to read, filter and export.</summary>
        public string       Categories  { get; set; } = "";

        // ── Repeats ───────────────────────────────────────────────────────────────────────

        /// <summary>None for an ordinary appointment. Set on a series master.</summary>
        public RepeatFreq Repeat { get; set; } = RepeatFreq.None;

        /// <summary>Every N days / weeks / months / years. Always at least 1.</summary>
        public int RepeatInterval { get; set; } = 1;

        /// <summary>Weekly only: which days are ticked. Empty means "the day the series starts on".</summary>
        public List<DayOfWeek> RepeatDays { get; set; } = [];

        /// <summary>Repeat until this date inclusive. Null with RepeatCount 0 means forever.</summary>
        public DateTime? RepeatUntil { get; set; }

        /// <summary>Stop after this many occurrences. 0 means no count limit.</summary>
        public int RepeatCount { get; set; }

        /// <summary>Occurrence start dates the user deleted one at a time. Compared by DATE, so a
        /// "just this one" delete survives the series later being moved to a different time.</summary>
        public List<DateTime> SkipDates { get; set; } = [];

        /// <summary>Set on an override row: the Id of the master whose occurrence this replaces.</summary>
        public Guid? SeriesId { get; set; }

        /// <summary>Set on an override row: which occurrence of the master this replaces, named by
        /// the start the master WOULD have generated. Not the same as Start once the user moves it.</summary>
        public DateTime? OccurrenceStart { get; set; }

        /// <summary>True on a series master - a row that stands for many dates.</summary>
        public bool IsSeries => Repeat != RepeatFreq.None && SeriesId == null;

        /// <summary>True on a row that replaces one date of a series.</summary>
        public bool IsOverride => SeriesId != null;

        /// <summary>True on an occurrence handed out by Recurrence.Expand rather than stored as a
        /// row. The views use this to decide whether editing needs the this-one-or-all question.</summary>
        public bool IsOccurrence => SeriesId != null || OccurrenceStart != null;

        /// <summary>The master this row or occurrence belongs to, or its own Id when it is one.</summary>
        public Guid SeriesKey => SeriesId ?? Id;

        // ──────────────────────────────────────────────────────────────────────────────────

        public bool SpansMultipleDays => End.Date > Start.Date;

        /// <summary>Duration, floored at zero so a malformed import cannot render backwards.</summary>
        public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

        public string TimeLabel
        {
            get
            {
                if (AllDay) return Services.LocaleManager.Loc("Str_Cal_AllDay");
                if (SpansMultipleDays)
                    return $"{Start:M/d h:mm tt} - {End:M/d h:mm tt}";
                return $"{Start:h:mm tt} - {End:h:mm tt}";
            }
        }

        /// <summary>True when this event covers any part of <paramref name="day"/>.</summary>
        public bool OccursOn(DateTime day)
        {
            var dayStart = day.Date;
            var dayEnd   = dayStart.AddDays(1);
            return Start < dayEnd && End > dayStart;
        }

        public CalendarEvent Clone() => new()
        {
            Id          = Id,
            Title       = Title,
            Start       = Start,
            End         = End,
            AllDay      = AllDay,
            Location    = Location,
            Description = Description,
            Attendees   = [.. Attendees],
            Categories  = Categories,
            Created     = Created,
            Modified    = Modified,

            // The two lists are COPIED, not shared. An occurrence handed to a view is a clone of
            // its master, and a view that edited a shared list would rewrite the series.
            Repeat          = Repeat,
            RepeatInterval  = RepeatInterval,
            RepeatDays      = [.. RepeatDays],
            RepeatUntil     = RepeatUntil,
            RepeatCount     = RepeatCount,
            SkipDates       = [.. SkipDates],
            SeriesId        = SeriesId,
            OccurrenceStart = OccurrenceStart
        };
    }
}
