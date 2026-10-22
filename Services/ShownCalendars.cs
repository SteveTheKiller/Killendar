using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// Other Killendars drawn alongside the open one, each in its own color. They are read only:
    /// new appointments, edits, drags and imports all go to the open Killendar, the one write
    /// target. Each is opened on its own connection, so an encrypted one stays locked until its
    /// own password is typed (never saved), a missing or unreadable one is listed as unavailable,
    /// and neither stops the others from showing. Locking the app closes them all.
    ///
    /// Remembered as "ShownCalendars": file name|#RRGGBB entries separated by ';'.
    /// </summary>
    public static class ShownCalendars
    {
        public const string Setting = "ShownCalendars";

        public enum State { Shown, Locked, Unavailable }

        public sealed class Item
        {
            public string Name { get; internal set; } = "";
            public string Color { get; internal set; } = "";
            public State State { get; internal set; }
            public string? Error { get; internal set; }
            internal EventStore? Store;
        }

        /// <summary>Colors handed out in turn, picked to stand apart from each other and from
        /// the category colors' red, green and blue.</summary>
        internal static readonly string[] Palette =
            ["#8E6CC9", "#D08A2E", "#2E9C94", "#C9568E", "#6B8E23", "#4F7CC4", "#B5654A", "#7F8C8D"];

        private static readonly List<Item> _items = [];

        public static IReadOnlyList<Item> Items => _items;

        /// <summary>Raised after the list or a calendar's state changes, so the views repaint.</summary>
        public static event Action? Changed;

        /// <summary>The remembered list, as name and color pairs, skipping duplicates and bad colors.</summary>
        internal static List<(string Name, string Color)> Parse(string? setting)
        {
            var result = new List<(string, string)>();
            foreach (var entry in (setting ?? "").Split(';'))
            {
                var parts = entry.Split('|');
                string name = parts[0].Trim();
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    result.Any(r => string.Equals(r.Item1, name, StringComparison.OrdinalIgnoreCase))) continue;
                string color = parts.Length > 1 && IsColor(parts[1].Trim()) ? parts[1].Trim() : Palette[result.Count % Palette.Length];
                result.Add((name, color));
            }
            return result;
        }

        internal static string Format(IEnumerable<(string Name, string Color)> entries) =>
            string.Join(";", entries.Select(e => e.Name + "|" + e.Color));

        private static bool IsColor(string s) =>
            s.Length == 7 && s[0] == '#' && int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

        /// <summary>
        /// Opens every remembered calendar except <paramref name="activeName"/>, which is already
        /// the open one. A calendar unlocked earlier this session stays open; anything no longer
        /// listed is closed.
        /// </summary>
        public static void Reload(string activeName)
        {
            var wanted = Parse(Settings.Get(Setting))
                .Where(e => !SameFile(e.Name, activeName)).ToList();
            foreach (var gone in _items.Where(i => !wanted.Any(w => SameFile(w.Name, i.Name))).ToList())
            {
                gone.Store?.Close();
                _items.Remove(gone);
            }
            var next = new List<Item>();
            foreach (var (name, color) in wanted)
            {
                var item = _items.FirstOrDefault(i => SameFile(i.Name, name)) ?? new Item { Name = name };
                item.Color = color;
                if (item.State != State.Shown || item.Store == null) Open(item, null);
                next.Add(item);
            }
            _items.Clear();
            _items.AddRange(next);
            Changed?.Invoke();
        }

        /// <summary>Opens a locked calendar with its password. False when the password is wrong.</summary>
        public static bool Unlock(Item item, string password)
        {
            Open(item, password);
            Changed?.Invoke();
            return item.State == State.Shown;
        }

        /// <summary>Starts or stops showing a calendar beside <paramref name="activeName"/>.</summary>
        public static void Toggle(string name, bool show, string activeName)
        {
            var entries = Parse(Settings.Get(Setting));
            entries.RemoveAll(e => SameFile(e.Name, name));
            if (show)
            {
                var used = entries.Select(e => e.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string color = Palette.FirstOrDefault(c => !used.Contains(c)) ?? Palette[entries.Count % Palette.Length];
                entries.Add((name, color));
            }
            Settings.Set(Setting, Format(entries));
            Reload(activeName);
        }

        /// <summary>Closes every shown calendar, as the app locks. Reload reopens the unencrypted ones.</summary>
        public static void CloseAll()
        {
            foreach (var item in _items)
            {
                item.Store?.Close();
                item.Store = null;
                if (item.State == State.Shown) item.State = State.Locked;
            }
            _items.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// <paramref name="own"/> (the open Killendar's appointments in a range) with every shown
        /// calendar's appointments in the same range added, each marked with its calendar's name
        /// and color.
        /// </summary>
        public static List<CalendarEvent> With(List<CalendarEvent> own, DateTime from, DateTime to)
        {
            if (_items.Count == 0) return own;
            foreach (var item in _items)
            {
                if (item.State != State.Shown || item.Store == null) continue;
                List<CalendarEvent> rows;
                try { rows = item.Store.GetInRange(from, to); }
                catch (Exception ex)
                {
                    item.State = State.Unavailable;
                    item.Error = ex.Message;
                    continue;
                }
                foreach (var row in rows)
                {
                    var shown = row.Clone();
                    shown.SourceCalendar = DisplayName(item.Name);
                    shown.SourceColor = item.Color;
                    own.Add(shown);
                }
            }
            return own;
        }

        /// <summary>A file name without its .kcal ending, for tooltips and the day panel.</summary>
        public static string DisplayName(string name) =>
            name.EndsWith(EventStore.Extension, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - EventStore.Extension.Length) : name;

        private static void Open(Item item, string? password)
        {
            item.Store?.Close();
            item.Store = null;
            item.Error = null;
            string path = Path.IsPathRooted(item.Name) ? item.Name : Path.Combine(EventStore.DataDir, item.Name);
            if (!File.Exists(path))
            {
                item.State = State.Unavailable;
                return;
            }
            if (password == null && EventStore.IsEncryptedFile(path))
            {
                item.State = State.Locked;
                return;
            }
            var store = new EventStore();
            try
            {
                store.OpenReadOnly(path, password);
                item.Store = store;
                item.State = State.Shown;
            }
            catch (Exception ex)
            {
                item.State = password != null ? State.Locked : State.Unavailable;
                item.Error = ex.Message;
            }
        }

        private static bool SameFile(string a, string b) =>
            string.Equals(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);
    }
}
