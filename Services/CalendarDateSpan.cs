using System;

namespace Killendar.Services
{
    /// <summary>Conversions between inclusive dates shown to people and half-open stored spans.</summary>
    internal static class CalendarDateSpan
    {
        internal static DateTime VisibleAllDayEnd(DateTime start, DateTime exclusiveEnd) =>
            exclusiveEnd > start ? exclusiveEnd.Date.AddDays(-1) : start.Date;

        internal static DateTime ExclusiveAllDayEnd(DateTime start, DateTime visibleEnd) =>
            (visibleEnd.Date < start.Date ? start.Date : visibleEnd.Date).AddDays(1);
    }
}
