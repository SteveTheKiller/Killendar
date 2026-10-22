using System;
using System.Collections.Generic;
using System.Linq;
using Killendar.Models;

namespace Killendar.Services
{
    public partial class EventStore
    {
        // ============================================================
        // "This and following". The series is cut in two at the chosen date: the first half
        // ends the day before, the second is an identical new series from that date carrying
        // every later single-date edit and skipped date. An edit then goes to the second half
        // as a whole-series edit, so moving, renaming and re-patterning work exactly as they do
        // for a whole series. The second half is a new appointment to other calendars (no
        // imported UID), the way a series split is sent out. One undo step either way.
        // ============================================================

        /// <summary>True for one date of a series that can be split there. A series kept on another
        /// time zone's clock (imported with one) only offers "the whole series".</summary>
        public bool CanSplit(CalendarEvent occurrence) =>
            GetSeriesMaster(occurrence) is CalendarEvent master && !Recurrence.UsesTimeBasis(master);

        /// <summary>Applies an edit to the chosen date and every later one.</summary>
        public void UpdateFollowing(CalendarEvent edited) => Recorded(() =>
        {
            var master = GetSeriesMaster(edited);
            if (master == null || Recurrence.UsesTimeBasis(master)) { UpdateSeriesRows(edited); return; }
            var nominal = Recurrence.CanonicalOccurrenceStart(master, edited);
            var split = SplitAt(master, nominal);
            if (split == null) { UpdateSeriesRows(edited); return; }   // the first date: the whole series
            if (split.Failed) return;                                   // nothing changed; LoadError says why
            var tail = split.Tail;
            var forTail = edited.Clone();
            forTail.Id = tail.Id;
            forTail.SeriesId = tail.Id;
            forTail.OccurrenceStart = tail.Start;
            forTail.ExternalUid = "";
            // "Ends after N times" as the series had it means the times left from here on.
            if (forTail.RepeatCount == master.RepeatCount) forTail.RepeatCount = tail.RepeatCount;
            // The extra dates as the series had them: the later half keeps only its own.
            if (forTail.ExtraDates.Select(d => d.Date).OrderBy(d => d).SequenceEqual(master.ExtraDates.Select(d => d.Date).OrderBy(d => d)))
                forTail.ExtraDates = [.. tail.ExtraDates];
            UpdateSeriesRows(forTail);
            if (LoadError != null) Unsplit(split);
        });

        /// <summary>Deletes the chosen date and every later one. Later single-date edits go to
        /// Recently deleted with the rest of what was removed.</summary>
        public void DeleteFollowing(CalendarEvent occurrence) => Recorded(() =>
        {
            var master = GetSeriesMaster(occurrence);
            if (master == null) return;
            var nominal = Recurrence.CanonicalOccurrenceStart(master, occurrence);
            var split = SplitAt(master, nominal);
            if (split == null) { DeleteSeriesRows(master.Id); return; }   // the first date: the whole series
            if (split.Failed) return;                                      // nothing changed; LoadError says why
            DeleteSeriesRows(split.Tail.Id);
            if (LoadError != null) Unsplit(split);
        });

        /// <summary>A split as written: the rows it replaced, so a failed follow-up can put them back.</summary>
        private sealed class Split
        {
            internal bool Failed;
            internal CalendarEvent Master = null!;
            internal CalendarEvent Tail = null!;
            internal List<CalendarEvent> MovedFrom = [];
        }

        /// <summary>
        /// The edit or delete after a split failed, so the split alone was saved. Puts the series
        /// back as it was, in one write, keeping the first failure's message: the user is told
        /// the change did not happen, and the calendar matches that.
        /// </summary>
        private void Unsplit(Split split)
        {
            string? error = LoadError;
            Commit(() =>
            {
                using var tx = _db!.BeginTransaction();
                Upsert(split.Master);
                foreach (var row in split.MovedFrom) Upsert(row);
                using (var cmd = _db.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM events WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", split.Tail.Id.ToString());
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            });
            // Commit reloaded the mirror on the first failure and clears the message on success.
            if (LoadError == null) LoadIntoMemory();
            LoadError = error ?? LoadError;
        }

        /// <summary>
        /// Ends <paramref name="master"/> before <paramref name="nominal"/> and starts an identical
        /// series there, moving later edits and skips to it. Null when nominal is the first date,
        /// where there is nothing to split; Failed when the write did not go through.
        /// </summary>
        private Split? SplitAt(CalendarEvent master, DateTime nominal)
        {
            var pattern = master.Clone();
            pattern.SkipDates.Clear();
            pattern.SkipOccurrences.Clear();
            int before = Recurrence.Expand(pattern, master.Start, nominal).Count(o => o.Start < nominal);
            if (before == 0) return null;

            var head = master.Clone();
            var tail = master.Clone();
            tail.Id = Guid.NewGuid();
            tail.ExternalUid = "";
            tail.Start = nominal;
            tail.End = nominal + (master.End - master.Start);
            if (NominalDuration.HasValue(tail)) tail.End = NominalDuration.EndAfterStartChange(tail, tail.Start);
            tail.Created = tail.Modified = head.Modified = DateTime.UtcNow;

            if (master.RepeatCount > 0)
            {
                head.RepeatCount = before;
                tail.RepeatCount = master.RepeatCount - before;
            }
            else
            {
                head.RepeatUntil = nominal.Date.AddDays(-1);
                head.RepeatUntilExact = null;
            }
            head.ExtraDates = master.ExtraDates.Where(d => d.Date < nominal.Date).ToList();
            tail.ExtraDates = master.ExtraDates.Where(d => d.Date >= nominal.Date).ToList();
            head.SkipDates = master.SkipDates.Where(d => d.Date < nominal.Date).ToList();
            tail.SkipDates = master.SkipDates.Where(d => d.Date >= nominal.Date).ToList();
            head.SkipOccurrences = master.SkipOccurrences.Where(d => d < nominal).ToList();
            tail.SkipOccurrences = master.SkipOccurrences.Where(d => d >= nominal).ToList();

            var moved = new List<CalendarEvent>();
            var movedFrom = new List<CalendarEvent>();
            foreach (var row in _events.Where(e => e.SeriesId == master.Id && e.OccurrenceStart.HasValue))
            {
                if (Recurrence.CanonicalOccurrenceStart(master, row) < nominal) continue;
                movedFrom.Add(row);
                var copy = row.Clone();
                copy.SeriesId = tail.Id;
                copy.Modified = DateTime.UtcNow;
                moved.Add(copy);
            }

            Commit(() =>
            {
                using var tx = _db!.BeginTransaction();
                Upsert(head);
                Upsert(tail);
                foreach (var row in moved) Upsert(row);
                tx.Commit();
                _events[_events.FindIndex(e => e.Id == master.Id)] = head;
                _events.Add(tail);
                foreach (var row in moved) _events[_events.FindIndex(e => e.Id == row.Id)] = row;
            });
            return new Split { Failed = LoadError != null, Master = master, Tail = tail, MovedFrom = movedFrom };
        }
    }
}
