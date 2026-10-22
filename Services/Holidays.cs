using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// Holidays come from a Killendar chosen as the holiday calendar, usually one imported from a
    /// government or company holiday .ics file: every all-day appointment in it is a holiday, its
    /// title the holiday's name. Nothing is built in and nothing is downloaded, so any country's
    /// or any employer's days work the same way.
    ///
    /// A holiday is a day off: it is shaded like one, and a repeat set to move off days off moves
    /// off it. The open Killendar can be the holiday calendar too. An encrypted one stays locked
    /// until its password is typed for this session, and holidays wait until then.
    ///
    /// Remembered as "HolidayCalendar": a file name in the data folder, or "" for none.
    /// </summary>
    public static class Holidays
    {
        public const string Setting = "HolidayCalendar";

        public enum State { None, Ready, Locked, Unavailable }

        public static State Status { get; private set; } = State.None;
        public static string Name { get; private set; } = "";

        private static EventStore? _store;          // a closed-file copy of another Killendar
        private static EventStore? _active;         // the open Killendar, when it is the one chosen
        private static readonly Dictionary<int, Dictionary<DateTime, string>> _years = [];

        /// <summary>Raised when the holidays change, so the views repaint.</summary>
        public static event Action? Changed;

        /// <summary>Reads the chosen calendar. <paramref name="open"/> is the open Killendar.</summary>
        public static void Load(EventStore open, string activeName)
        {
            Close();
            Name = Settings.Get(Setting) ?? "";
            if (Name.Length == 0) { Status = State.None; Changed?.Invoke(); return; }
            if (string.Equals(Path.GetFileName(Name), Path.GetFileName(activeName), StringComparison.OrdinalIgnoreCase))
            {
                _active = open;
                Status = State.Ready;
                Changed?.Invoke();
                return;
            }
            Open(null);
        }

        /// <summary>Opens a locked holiday calendar with its password. False when it is wrong.</summary>
        public static bool Unlock(string password)
        {
            Open(password);
            return Status == State.Ready;
        }

        /// <summary>The open Killendar's appointments changed: holidays in it may have too.</summary>
        public static void Refresh()
        {
            if (_active == null) return;
            _years.Clear();
            Changed?.Invoke();
        }

        /// <summary>Forgets the holidays read from another file, as the app locks.</summary>
        public static void Close()
        {
            _store?.Close();
            _store = null;
            _active = null;
            _years.Clear();
            if (Status == State.Ready) Status = State.None;
        }

        /// <summary>Chooses the holiday calendar, or none with "".</summary>
        public static void Choose(string name, EventStore open, string activeName)
        {
            Settings.Set(Setting, name ?? "");
            Load(open, activeName);
        }

        public static bool IsHoliday(DateTime day) => NameOf(day) != null;

        /// <summary>The holiday's name on <paramref name="day"/>, or null.</summary>
        public static string? NameOf(DateTime day)
        {
            if (Status != State.Ready) return null;
            var year = YearOf(day.Year);
            return year.TryGetValue(day.Date, out var name) ? name : null;
        }

        [ThreadStatic] private static bool _reading;

        private static Dictionary<DateTime, string> YearOf(int year)
        {
            if (_years.TryGetValue(year, out var known)) return known;
            // Reading the holiday calendar expands its own repeats, and a repeat there that moves
            // off days off would ask for holidays again: that inner ask sees none.
            if (_reading) return [];
            _reading = true;
            try { return ReadYear(year); }
            finally { _reading = false; }
        }

        private static Dictionary<DateTime, string> ReadYear(int year)
        {
            var days = new Dictionary<DateTime, string>();
            var source = _active ?? _store;
            if (source != null && year > 1 && year < 9999)
            {
                var from = new DateTime(year, 1, 1);
                foreach (var ev in source.GetInRange(from, from.AddYears(1)).Where(e => e.AllDay))
                {
                    var last = CalendarDateSpan.VisibleAllDayEnd(ev.Start, ev.End).Date;
                    for (var d = ev.Start.Date; d <= last && d.Year <= year; d = d.AddDays(1))
                        if (d.Year == year && !days.ContainsKey(d))
                            days[d] = string.IsNullOrWhiteSpace(ev.Title) ? LocaleManager.Loc("Str_Hol_Holiday") : ev.Title;
                }
            }
            _years[year] = days;
            return days;
        }

        private static void Open(string? password)
        {
            _store?.Close();
            _store = null;
            _years.Clear();
            string path = Path.Combine(EventStore.DataDir, Path.GetFileName(Name));
            if (!File.Exists(path)) { Status = State.Unavailable; Changed?.Invoke(); return; }
            if (password == null && EventStore.IsEncryptedFile(path)) { Status = State.Locked; Changed?.Invoke(); return; }
            var store = new EventStore();
            try
            {
                store.OpenReadOnly(path, password);
                store.ReleaseFile();
                _store = store;
                Status = State.Ready;
            }
            catch (Exception)
            {
                Status = password != null ? State.Locked : State.Unavailable;
            }
            Changed?.Invoke();
        }
    }
}
