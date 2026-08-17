using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Killendar.Models;

namespace Killendar.Services
{
    public partial class EventStore
    {
        // ============================================================
        // Queries. Served from memory - identical semantics to the JSON store.
        // ============================================================

        public CalendarEvent? GetById(Guid id) => _events.FirstOrDefault(e => e.Id == id);

        /// <summary>The series master an occurrence belongs to, or null.</summary>
        public CalendarEvent? GetSeriesMaster(CalendarEvent ev)
        {
            if (ev == null) return null;
            var key = ev.SeriesKey;
            return _events.FirstOrDefault(e => e.Id == key && e.IsSeries);
        }

        /// <summary>
        /// All appointments overlapping the half-open interval [start, end), earliest first.
        ///
        /// This is where repeats become visible. A series is ONE stored row; its dates are
        /// generated here, so every view and the day agenda got repeats the moment this method
        /// did, without any of them changing. Three kinds of row are handled:
        ///   - plain appointments, included when they overlap, exactly as before;
        ///   - series masters, expanded into occurrences by Recurrence;
        ///   - overrides, which stand in for one generated date and are included on their own
        ///     dates - so an occurrence moved to another day appears there, not here.
        /// </summary>
        public List<CalendarEvent> GetInRange(DateTime start, DateTime end)
        {
            var result = new List<CalendarEvent>();

            // Which generated dates have been replaced by an edited row. Keyed by date, because
            // the override's own Start is wherever the user moved it to, and the thing being
            // replaced is the date the series WOULD have produced.
            var replaced = new HashSet<(Guid Series, DateTime Day)>();
            foreach (var e in _events)
                if (e.IsOverride && e.OccurrenceStart.HasValue)
                    replaced.Add((e.SeriesId!.Value, e.OccurrenceStart.Value.Date));

            foreach (var e in _events)
            {
                if (e.IsSeries)
                {
                    foreach (var occ in Recurrence.Expand(e, start, end))
                        if (!replaced.Contains((e.Id, occ.OccurrenceStart!.Value.Date)))
                            result.Add(occ);
                    continue;
                }

                // Plain appointments AND overrides: both are real rows shown on their own dates.
                if (e.Start < end && e.End > start) result.Add(e);
            }

            return [.. result.OrderBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Start)];
        }

        /// <summary>All events touching a calendar date, all-day entries first.</summary>
        public List<CalendarEvent> GetOnDay(DateTime date)
            => GetInRange(date.Date, date.Date.AddDays(1));

        /// <summary>
        /// The next <paramref name="count"/> appointments starting at or after
        /// <paramref name="from"/>. Repeats are included, which is why this walks forward a window
        /// at a time instead of reading the row list: a series has no rows to read.
        /// </summary>
        public List<CalendarEvent> GetUpcoming(DateTime from, int count)
        {
            if (count <= 0) return [];

            // Widening windows rather than one huge range, so the common case (something on in the
            // next fortnight) does not expand five years of every series to find it.
            foreach (int days in new[] { 14, 90, 400, 1500 })
            {
                var found = GetInRange(from, from.AddDays(days))
                            .Where(e => e.End > from)
                            .OrderBy(e => e.Start)
                            .Take(count)
                            .ToList();
                if (found.Count >= count) return found;

                // Last window: return what there is rather than looping forever on a calendar
                // that genuinely has nothing further out.
                if (days == 1500) return found;
            }
            return [];
        }
    }
}
