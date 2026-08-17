using System;
using System.Collections.Generic;
using System.Linq;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// Pure calendar layout calculations. Views consume these immutable placements and remain
    /// responsible only for WPF controls, brushes, and pointer interaction.
    /// </summary>
    internal static class CalendarLayout
    {
        internal sealed class TimedPlacement
        {
            internal CalendarEvent Event { get; set; } = null!;
            internal DateTime VisibleStart { get; set; }
            internal DateTime VisibleEnd { get; set; }
            internal int Lane { get; set; }
            internal int LaneCount { get; set; }
        }

        internal sealed class AllDayPlacement
        {
            internal CalendarEvent Event { get; set; } = null!;
            internal int StartColumn { get; set; }
            internal int ColumnSpan { get; set; }
            internal int Row { get; set; }
        }

        /// <summary>
        /// Clips all-day appointments to a visible run of day columns. Events that overlap in
        /// columns receive separate rows; a multi-day event remains one continuous placement.
        /// </summary>
        internal static List<AllDayPlacement> PlaceAllDayRange(
            IEnumerable<CalendarEvent> events, DateTime rangeStart, int dayCount)
        {
            if (dayCount <= 0) return [];
            var start = rangeStart.Date;
            var end = start.AddDays(dayCount);
            var placements = events
                .Where(e => e.AllDay && e.Start < end && e.End > start)
                .Select(e =>
                {
                    var visibleStart = e.Start < start ? start : e.Start.Date;
                    var visibleEnd = e.End > end ? end : e.End;
                    int first = Math.Max(0, (visibleStart.Date - start).Days);
                    int afterLast = Math.Min(dayCount,
                        (int)Math.Ceiling((visibleEnd - start).TotalDays));
                    return new AllDayPlacement
                    {
                        Event = e,
                        StartColumn = first,
                        ColumnSpan = Math.Max(1, afterLast - first),
                    };
                })
                .OrderBy(p => p.StartColumn)
                .ThenByDescending(p => p.ColumnSpan)
                .ToList();

            var rowEnds = new List<int>();
            foreach (var placement in placements)
            {
                int row = rowEnds.FindIndex(last => last <= placement.StartColumn);
                if (row < 0)
                {
                    row = rowEnds.Count;
                    rowEnds.Add(placement.StartColumn + placement.ColumnSpan);
                }
                else rowEnds[row] = placement.StartColumn + placement.ColumnSpan;
                placement.Row = row;
            }
            return placements;
        }

        internal static bool IsMultiDay(CalendarEvent e)
        {
            if (e.End <= e.Start) return false;
            var lastCovered = e.End.TimeOfDay == TimeSpan.Zero
                ? e.End.AddTicks(-1).Date
                : e.End.Date;
            return lastCovered > e.Start.Date;
        }

        /// <summary>Continuous month-row placements for appointments covering multiple dates.</summary>
        internal static List<AllDayPlacement> PlaceMultiDayRange(
            IEnumerable<CalendarEvent> events, DateTime rangeStart, int dayCount)
        {
            var copies = events.Where(IsMultiDay).Select(e =>
            {
                var end = e.End.TimeOfDay == TimeSpan.Zero ? e.End : e.End.Date.AddDays(1);
                return new CalendarEvent
                {
                    Id = e.Id, SeriesId = e.SeriesId, OccurrenceStart = e.OccurrenceStart,
                    Title = e.Title, Start = e.Start.Date, End = end,
                    AllDay = true, Categories = e.Categories,
                    Location = e.Location, Description = e.Description, Attendees = e.Attendees,
                };
            });
            var byId = events.Where(IsMultiDay).ToDictionary(e =>
                (e.Id, e.OccurrenceStart?.Ticks ?? 0L));
            var placements = PlaceAllDayRange(copies, rangeStart, dayCount);
            foreach (var p in placements)
                if (byId.TryGetValue((p.Event.Id, p.Event.OccurrenceStart?.Ticks ?? 0L), out var original))
                    p.Event = original;
            return placements;
        }

        /// <summary>
        /// Clips timed appointments to one visible day and assigns overlap lanes per connected
        /// cluster. The calculation has no WPF dependency, so date-boundary behavior can be tested
        /// without constructing a window.
        /// </summary>
        internal static List<TimedPlacement> PlaceTimedDay(
            IEnumerable<CalendarEvent> events, DateTime day)
        {
            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);
            var visible = events
                .Where(ev => !ev.AllDay && ev.Start < dayEnd && ev.End > dayStart)
                .Select(ev => new TimedPlacement
                {
                    Event = ev,
                    VisibleStart = ev.Start < dayStart ? dayStart : ev.Start,
                    VisibleEnd = ev.End > dayEnd ? dayEnd : ev.End,
                })
                .OrderBy(p => p.VisibleStart)
                .ThenBy(p => p.VisibleEnd)
                .ToList();

            var laneEnds = new List<DateTime>();
            var clusterOf = new int[visible.Count];
            var clusterLanes = new List<int>();
            int cluster = -1;
            var clusterEnd = DateTime.MinValue;

            for (int i = 0; i < visible.Count; i++)
            {
                var item = visible[i];
                int lane = laneEnds.FindIndex(end => end <= item.VisibleStart);
                if (lane < 0)
                {
                    lane = laneEnds.Count;
                    laneEnds.Add(item.VisibleEnd);
                }
                else laneEnds[lane] = item.VisibleEnd;
                item.Lane = lane;

                if (item.VisibleStart >= clusterEnd)
                {
                    cluster++;
                    clusterLanes.Add(0);
                    clusterEnd = item.VisibleEnd;
                }
                else if (item.VisibleEnd > clusterEnd) clusterEnd = item.VisibleEnd;

                clusterOf[i] = cluster;
                clusterLanes[cluster] = Math.Max(clusterLanes[cluster], lane + 1);
            }

            for (int i = 0; i < visible.Count; i++)
                visible[i].LaneCount = Math.Max(1, clusterLanes[clusterOf[i]]);

            return visible;
        }
    }
}
