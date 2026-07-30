using System;
using System.Collections.Generic;

namespace Killendar.Models
{
    /// <summary>
    /// One appointment. Field names are load-bearing: IcsService maps them straight onto
    /// RFC-5545 VEVENT properties, and EventStore serialises them as-is, so renaming a
    /// property silently breaks both ICS round-tripping and every previously saved file.
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
        public List<string> Attendees   { get; set; } = new List<string>();
        public DateTime     Created     { get; set; } = DateTime.UtcNow;
        public DateTime     Modified    { get; set; } = DateTime.UtcNow;

        public bool SpansMultipleDays => End.Date > Start.Date;

        /// <summary>Duration, floored at zero so a malformed import cannot render backwards.</summary>
        public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

        public string TimeLabel
        {
            get
            {
                if (AllDay) return Killendar.MainWindow.LocStatic("Str_Cal_AllDay");
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

        public CalendarEvent Clone() => new CalendarEvent
        {
            Id          = Id,
            Title       = Title,
            Start       = Start,
            End         = End,
            AllDay      = AllDay,
            Location    = Location,
            Description = Description,
            Attendees   = new List<string>(Attendees),
            Created     = Created,
            Modified    = Modified
        };
    }
}
