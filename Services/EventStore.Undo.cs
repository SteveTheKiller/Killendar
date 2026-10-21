using System;
using System.Collections.Generic;
using Killendar.Models;

namespace Killendar.Services
{
    public partial class EventStore
    {
        // ============================================================
        // Undo. Every mutation of an appointment replaces the stored object rather than editing
        // it (the editor and drags work on clones; series edits build new rows). So a step is
        // simply which rows changed from which object to which, and undoing it writes the old
        // objects back. No copies are made and nothing about recurrence identity is recomputed:
        // the exact rows that were there come back.
        //
        // The history lives in memory, 50 steps deep, and belongs to the open file. Opening or
        // closing a Killendar, a failed write, and a category definition change all forget it,
        // because each changes rows in ways a step did not record.
        // ============================================================

        private const int UndoDepth = 50;

        private sealed class UndoStep
        {
            internal readonly List<(Guid Id, CalendarEvent? Before, CalendarEvent? After)> Rows = [];
        }

        private readonly LinkedList<UndoStep> _undo = new();
        private readonly Stack<UndoStep> _redo = new();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        internal void ForgetHistory()
        {
            _undo.Clear();
            _redo.Clear();
        }

        private void Recorded(Action change) => Recorded(() => { change(); return 0; });

        /// <summary>Runs one user change and records the rows it replaced, added or removed.</summary>
        private T Recorded<T>(Func<T> change)
        {
            var before = new Dictionary<Guid, CalendarEvent>(_events.Count);
            foreach (var ev in _events) before[ev.Id] = ev;
            T result = change();
            if (LoadError != null) return result;   // the write failed and the mirror was reloaded

            var step = new UndoStep();
            var after = new Dictionary<Guid, CalendarEvent>(_events.Count);
            foreach (var ev in _events) after[ev.Id] = ev;
            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out var now)) step.Rows.Add((pair.Key, pair.Value, null));
                else if (!ReferenceEquals(now, pair.Value)) step.Rows.Add((pair.Key, pair.Value, now));
            }
            foreach (var pair in after)
                if (!before.ContainsKey(pair.Key)) step.Rows.Add((pair.Key, null, pair.Value));
            if (step.Rows.Count == 0) return result;

            _undo.AddLast(step);
            if (_undo.Count > UndoDepth) _undo.RemoveFirst();
            _redo.Clear();
            return result;
        }

        /// <summary>Puts back the rows the last change replaced. False when there is nothing to
        /// undo or the write failed (LoadError says why).</summary>
        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var step = _undo.Last!.Value;
            _undo.RemoveLast();
            if (!Apply(step, back: true)) return false;
            _redo.Push(step);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var step = _redo.Pop();
            if (!Apply(step, back: false)) return false;
            _undo.AddLast(step);
            return true;
        }

        private bool Apply(UndoStep step, bool back)
        {
            return Commit(() =>
            {
                using (var tx = _db!.BeginTransaction())
                {
                    foreach (var (id, before, after) in step.Rows)
                    {
                        var target = back ? before : after;
                        if (target != null) Upsert(target);
                        else
                        {
                            using var cmd = _db.CreateCommand();
                            cmd.CommandText = "DELETE FROM events WHERE id = $id";
                            cmd.Parameters.AddWithValue("$id", id.ToString());
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
                // The mirror follows only once the database has the step.
                foreach (var (id, before, after) in step.Rows)
                {
                    var target = back ? before : after;
                    int index = _events.FindIndex(e => e.Id == id);
                    if (target == null) { if (index >= 0) _events.RemoveAt(index); }
                    else if (index >= 0) _events[index] = target;
                    else _events.Add(target);
                }
            });
        }
    }
}
