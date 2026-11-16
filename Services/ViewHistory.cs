using System;
using System.Collections.Generic;

namespace Killendar.Services
{
    /// <summary>
    /// Back and forward through the places the calendar has shown, like a browser. A spot is a
    /// view and the date it was looking at. Switching views and jumping to a date are visits;
    /// paging with Previous and Next is not, so Back skips the pages in between.
    /// </summary>
    internal sealed class ViewHistory
    {
        internal readonly struct Spot : IEquatable<Spot>
        {
            public Spot(string view, DateTime anchor) { View = view; Anchor = anchor; }
            public string View { get; }
            public DateTime Anchor { get; }
            public bool Equals(Spot other) => View == other.View && Anchor == other.Anchor;
            public override bool Equals(object? obj) => obj is Spot s && Equals(s);
            public override int GetHashCode() => (View?.GetHashCode() ?? 0) ^ Anchor.GetHashCode();
        }

        /// <summary>How many steps back are kept.</summary>
        internal const int MaxSteps = 50;

        private readonly LinkedList<Spot> _back = new();
        private readonly Stack<Spot> _forward = new();

        public bool CanGoBack => _back.Count > 0;
        public bool CanGoForward => _forward.Count > 0;

        /// <summary>Records <paramref name="leaving"/> as the place Back returns to.</summary>
        public void Visit(Spot leaving)
        {
            Push(leaving);
            _forward.Clear();
        }

        /// <summary>The spot before <paramref name="here"/>, or null when there is none.</summary>
        public Spot? Back(Spot here)
        {
            if (_back.Count == 0) return null;
            var to = _back.Last!.Value;
            _back.RemoveLast();
            _forward.Push(here);
            return to;
        }

        /// <summary>The spot Back left, or null when there is none.</summary>
        public Spot? Forward(Spot here)
        {
            if (_forward.Count == 0) return null;
            var to = _forward.Pop();
            Push(here);
            return to;
        }

        private void Push(Spot spot)
        {
            _back.AddLast(spot);
            while (_back.Count > MaxSteps) _back.RemoveFirst();
        }
    }
}
