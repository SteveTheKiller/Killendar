using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>Which appointments a search looks at, by when they happen.</summary>
    public enum SearchSpan { Any, Upcoming, Past }

    public partial class EventStore
    {
        // ============================================================
        // Search. A scan of the in-memory mirror, never an index: an index file would sit beside
        // an encrypted calendar in the clear, and a few thousand appointments scan in well under
        // a frame anyway.
        // ============================================================

        private static readonly TimeSpan SearchReach = TimeSpan.FromDays(366 * 5);
        private static readonly CompareInfo SearchCompare = CultureInfo.InvariantCulture.CompareInfo;

        /// <summary>Appointments where every word of the query appears in the title, location,
        /// description, attendees or categories, ignoring case and accents. A repeating one is
        /// listed once, at its next date (or its latest, for Past). Upcoming first, soonest
        /// first, then past ones, latest first.</summary>
        public List<CalendarEvent> Search(string query, SearchSpan span, DateTime now, int limit = 200)
        {
            var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return [];
            var upcoming = new List<CalendarEvent>();
            var past = new List<CalendarEvent>();
            foreach (var row in _events)
            {
                if (!Matches(row, terms)) continue;
                if (row.IsSeries)
                {
                    if (span != SearchSpan.Past &&
                        Recurrence.Expand(row, now, now + SearchReach).FirstOrDefault(o => o.End > now) is CalendarEvent next)
                    {
                        upcoming.Add(next);
                        continue;
                    }
                    if (span != SearchSpan.Upcoming &&
                        Recurrence.Expand(row, now - SearchReach, now).LastOrDefault(o => o.End <= now) is CalendarEvent last)
                        past.Add(last);
                    continue;
                }
                if (row.End > now) { if (span != SearchSpan.Past) upcoming.Add(row); }
                else if (span != SearchSpan.Upcoming) past.Add(row);
            }
            return [.. upcoming.OrderBy(e => e.Start).Concat(past.OrderByDescending(e => e.Start)).Take(limit)];
        }

        private static bool Matches(CalendarEvent ev, string[] terms)
        {
            string[] fields = [ev.Title, ev.Location, ev.Description, string.Join(" ", ev.Attendees), ev.Categories];
            return terms.All(term => fields.Any(field => !string.IsNullOrEmpty(field) &&
                SearchCompare.IndexOf(field, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0));
        }
    }
}
