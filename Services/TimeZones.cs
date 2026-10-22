using System;
using System.Collections.Generic;
using System.Linq;
using Killendar.Models;
using TimeZoneConverter;

namespace Killendar.Services
{
    /// <summary>
    /// Time zones are off until "Show time zones" is ticked in the date settings. Then the editor
    /// offers a time zone for each appointment, its times are typed on that zone's clock, and it
    /// keeps that clock: a 9:00 meeting in New York stays at 9:00 there across both places' clock
    /// changes. Week and Day can also show a second zone's hours beside this computer's.
    ///
    /// An appointment's zone is the source-zone time basis imported calendars already use
    /// (RepeatTimeBasis, RepeatAnchor, RepeatZoneId), so a zone picked here and a TZID read from a
    /// file are the same thing and export the same way. "" means this computer's own zone.
    /// </summary>
    public static class TimeZones
    {
        public const string ShowSetting = "ShowTimeZones";
        public const string SecondSetting = "SecondTimeZone";

        public static bool Show { get; private set; }

        /// <summary>The extra zone Week and Day show, or null for none.</summary>
        public static TimeZoneInfo? Second { get; private set; }

        public static void Load(string? show, string? second)
        {
            Show = show == "1";
            Second = Show ? Find(second) : null;
        }

        public static IReadOnlyList<TimeZoneInfo> All => TimeZoneInfo.GetSystemTimeZones();

        /// <summary>A zone by Windows or IANA id; null for "", unknown ids and this computer's own zone.</summary>
        public static TimeZoneInfo? Find(string? id)
        {
            if (id is null || string.IsNullOrWhiteSpace(id) || !TZConvert.TryGetTimeZoneInfo(id, out var zone)) return null;
            return zone;
        }

        /// <summary>The zone an appointment keeps its clock in, or "" for this computer's.</summary>
        public static string ZoneOf(CalendarEvent ev) =>
            !ev.AllDay && ev.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone && ev.RepeatAnchor.HasValue &&
            Find(ev.RepeatZoneId) != null ? ev.RepeatZoneId : "";

        /// <summary>A local time on <paramref name="zone"/>'s clock.</summary>
        public static DateTime ToZone(DateTime local, TimeZoneInfo zone) =>
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(local.ToUniversalTime(), zone), DateTimeKind.Unspecified);

        /// <summary>
        /// A time on <paramref name="zone"/>'s clock as this computer's local time. A time that
        /// happens twice there is the first one; one the clock skips is read as an hour later,
        /// the same rule imported calendars follow.
        /// </summary>
        public static DateTime FromZone(DateTime clock, TimeZoneInfo zone) =>
            Recurrence.FromTimeBasis(Basis(zone.Id), clock).ToLocalTime();

        /// <summary>Gives an appointment its zone, or takes it back to this computer's clock.</summary>
        public static void Apply(CalendarEvent ev, string zoneId)
        {
            if (ev.AllDay) return;
            if (Find(zoneId) == null)
            {
                if (ev.RepeatTimeBasis != RecurrenceTimeBasis.SourceZone) return;
                ev.RepeatTimeBasis = RecurrenceTimeBasis.Local;
                ev.RepeatAnchor = null;
                ev.RepeatZoneId = "";
                return;
            }
            ev.RepeatTimeBasis = RecurrenceTimeBasis.SourceZone;
            ev.RepeatZoneId = zoneId;
            ev.RepeatAnchor = Recurrence.ToTimeBasis(ev, ev.Start);
        }

        /// <summary>A short name for a gutter heading: "UTC+05:30".</summary>
        public static string Offset(TimeZoneInfo zone, DateTime local)
        {
            var offset = zone.GetUtcOffset(local.ToUniversalTime());
            if (offset == TimeSpan.Zero) return "UTC";
            return "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm");
        }

        /// <summary>An hour label on another zone's clock: "3 PM", or "3:30 PM" off the hour.</summary>
        public static string HourLabel(DateTime clock) =>
            clock.Minute == 0 ? DateFormatManager.HourLabel(clock.Hour) : DateFormatManager.Time(clock);

        private static CalendarEvent Basis(string id) =>
            new() { RepeatTimeBasis = RecurrenceTimeBasis.SourceZone, RepeatZoneId = id };

        internal static IEnumerable<TimeZoneInfo> Choices(string current)
        {
            var all = All.ToList();
            if (Find(current) is TimeZoneInfo extra && !all.Any(z => z.Id == extra.Id)) all.Add(extra);
            return all;
        }
    }
}
