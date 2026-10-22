using System;
using System.Globalization;
using Killendar.Models;

namespace Killendar.Services
{
    internal static class NominalDuration
    {
        internal static bool HasValue(CalendarEvent ev) => ev.DurationDays > 0;

        internal static DateTime SourceStart(CalendarEvent ev, DateTime localStart)
        {
            if (!Recurrence.UsesTimeBasis(ev)) return DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified);
            if (ev.RepeatAnchor.HasValue && Recurrence.FromTimeBasis(ev, ev.RepeatAnchor.Value) == localStart.ToUniversalTime())
                return ev.RepeatAnchor.Value;
            return Recurrence.ToTimeBasis(ev, localStart);
        }

        internal static DateTime EndAfterStartChange(CalendarEvent ev, DateTime localStart) =>
            EndLocal(ev, SourceStart(ev, localStart));

        internal static DateTime EndLocal(CalendarEvent ev, DateTime nominalStart)
        {
            var boundary = nominalStart.AddDays(ev.DurationDays);
            var utc = Recurrence.UsesTimeBasis(ev) ? Recurrence.FromTimeBasis(ev, boundary)
                : DateTime.SpecifyKind(boundary, DateTimeKind.Local).ToUniversalTime();
            return utc.AddTicks(ev.DurationTimeTicks).ToLocalTime();
        }

        internal static long LookBackTicks(CalendarEvent ev, long legacyTicks)
        {
            if (!HasValue(ev)) return legacyTicks;
            long maximum = DateTime.MaxValue.Ticks;
            long days = (long)ev.DurationDays + 2;
            if (days > maximum / TimeSpan.TicksPerDay) return maximum;
            long ticks = days * TimeSpan.TicksPerDay;
            return ev.DurationTimeTicks > maximum - ticks ? maximum : ticks + ev.DurationTimeTicks;
        }

        internal static string Format(CalendarEvent ev)
        {
            if (ev.DurationTimeTicks < 0 || ev.DurationTimeTicks % TimeSpan.TicksPerSecond != 0)
                throw new NotSupportedException("A calendar duration with fractional or negative subday time cannot be exported.");
            // "P1D", "P1DT2H": the time part only when there is one, in the RFC 5545 grammar.
            string days = "P" + ev.DurationDays.ToString(CultureInfo.InvariantCulture) + "D";
            return ev.DurationTimeTicks == 0 ? days
                : days + IcsService.TimeDuration(TimeSpan.FromTicks(ev.DurationTimeTicks)).Substring(1);
        }
    }
}
