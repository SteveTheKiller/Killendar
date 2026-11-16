using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Killendar.Services
{
    /// <summary>The fields a typed line fills in. AllDay results carry the date in Start and End.</summary>
    internal sealed class QuickAddResult
    {
        public string Title { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool AllDay { get; set; }
        public string Location { get; set; } = "";
        public string Categories { get; set; } = "";
    }

    /// <summary>The handful of keywords the sentence box reads, in the app's language. Weekday and
    /// month names come from the culture, so only these need translating.</summary>
    internal sealed class QuickAddWords
    {
        public QuickAddWords(string today, string tomorrow, string next, string at, string forWord, string allDay, string noon)
        {
            Today = Clean(today); Tomorrow = Clean(tomorrow); Next = Clean(next); At = Clean(at);
            For = Clean(forWord); AllDay = Clean(allDay); Noon = Clean(noon);
        }

        public static QuickAddWords English { get; } =
            new("today", "tomorrow", "next", "at", "for", "all day", "noon");

        public string Today { get; }
        public string Tomorrow { get; }
        public string Next { get; }
        public string At { get; }
        public string For { get; }
        public string AllDay { get; }
        public string Noon { get; }

        private static string Clean(string s) => (s ?? "").Trim();
    }

    /// <summary>
    /// Reads "lunch with Dave fri 12:30 at Mel's" into a title, a start and end, a place and
    /// categories. Plain rules, no guessing service: days and dates, clock times and ranges,
    /// "for" a length, "at" a place, "all day" and #category. Whatever is left is the title.
    /// </summary>
    internal static class QuickAdd
    {
        private static readonly TimeSpan DefaultLength = TimeSpan.FromHours(1);

        private static readonly Regex Clock = new(
            @"^(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<s>am|pm|a|p)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Range = new(
            @"^(?<a>\d{1,2}(?::\d{2})?(?:am|pm|a|p)?)-(?<b>\d{1,2}(?::\d{2})?(?:am|pm|a|p)?)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Length = new(
            @"^(?<n>\d+(?:[.,]\d+)?)\s*(?<u>h|hr|hrs|hour|hours|m|min|mins|minute|minutes)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Unit = new(
            @"^(h|hr|hrs|hour|hours|m|min|mins|minute|minutes)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex NumericDate = new(
            @"^(?<a>\d{1,4})[/.](?<b>\d{1,2})(?:[/.](?<c>\d{2,4}))?\.?$", RegexOptions.CultureInvariant);
        private static readonly Regex DayNumber = new(@"^(?<d>\d{1,2})(?:\.|st|nd|rd|th)?,?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Year = new(@"^(?<y>\d{4}),?$", RegexOptions.CultureInvariant);

        public static QuickAddResult Parse(string text, DateTime now, CultureInfo culture, QuickAddWords words)
        {
            var tokens = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
            var used = new bool[tokens.Count];
            var result = new QuickAddResult();
            DateTime? date = null;
            TimeSpan? start = null, end = null, length = null;
            bool allDay = false;
            var categories = new List<string>();

            // Pass 1: everything that is not a place or the title.
            for (int i = 0; i < tokens.Count; i++)
            {
                if (used[i]) continue;
                string t = tokens[i];
                string w = Word(t);

                if (t.Length > 1 && t[0] == '#')
                {
                    categories.Add(t.Substring(1).TrimEnd(',', '.'));
                    used[i] = true;
                    continue;
                }
                if (MatchPhrase(tokens, used, i, words.AllDay, culture) is int allDayLength)
                {
                    allDay = true;
                    Mark(used, i, allDayLength);
                    continue;
                }
                if (Same(w, words.For, culture) && i + 1 < tokens.Count && ReadLength(tokens, i + 1, out var len, out int lenTokens))
                {
                    length = len;
                    Mark(used, i, 1 + lenTokens);
                    continue;
                }
                if (Same(w, words.Today, culture)) { date = now.Date; used[i] = true; continue; }
                if (Same(w, words.Tomorrow, culture)) { date = now.Date.AddDays(1); used[i] = true; continue; }
                if (Same(w, words.Next, culture) && i + 1 < tokens.Count && Weekday(Word(tokens[i + 1]), culture) is DayOfWeek nd)
                {
                    date = NextWeekday(now.Date, nd).AddDays(7);
                    Mark(used, i, 2);
                    continue;
                }
                if (Weekday(w, culture) is DayOfWeek wd) { date = NextWeekday(now.Date, wd); used[i] = true; continue; }
                if (ReadMonthDate(tokens, i, now, culture, out var md, out int mdTokens)) { date = md; Mark(used, i, mdTokens); continue; }
                if (ReadNumericDate(t, now, culture, out var numeric)) { date = numeric; used[i] = true; continue; }
                if (Same(w, words.Noon, culture)) { start = TimeSpan.FromHours(12); used[i] = true; continue; }
                var range = Range.Match(w);
                if (range.Success && ReadRange(range.Groups["a"].Value, range.Groups["b"].Value, out var rs, out var re))
                {
                    start = rs; end = re; used[i] = true;
                    continue;
                }
                if (ReadClock(t, requireMark: true, null, out var clock, out _)) { start = clock; used[i] = true; continue; }
            }

            // Pass 2: "at" a place, unless it is "at 4pm". The place runs to the next word that
            // pass 1 already read, so "coffee at Joe's Diner tomorrow 8am" stops before tomorrow.
            for (int i = 0; i < tokens.Count; i++)
            {
                if (used[i] || !Same(Word(tokens[i]), words.At, culture)) continue;
                if (i + 1 >= tokens.Count) break;
                if (used[i + 1]) { used[i] = true; continue; }
                int j = i + 1;
                var place = new List<string>();
                while (j < tokens.Count && !used[j]) { place.Add(tokens[j]); used[j] = true; j++; }
                used[i] = true;
                result.Location = string.Join(" ", place).Trim().TrimEnd(',');
                break;
            }

            result.Title = string.Join(" ", tokens.Where((_, k) => !used[k])).Trim().TrimEnd(',');
            result.Categories = string.Join(", ", categories.Where(c => c.Length > 0));

            if (allDay || (date != null && start == null))
            {
                var day = date ?? now.Date;
                result.AllDay = true;
                result.Start = day;
                result.End = day;
                return result;
            }

            if (start == null)
            {
                var nextHour = now.Date.AddHours(now.Hour + 1);
                result.Start = date == null ? nextHour : date.Value + nextHour.TimeOfDay;
            }
            else
            {
                var day = date ?? now.Date;
                if (date == null && day + start.Value <= now) day = day.AddDays(1);
                result.Start = day + start.Value;
            }

            if (end != null)
            {
                var e = result.Start.Date + end.Value;
                result.End = e > result.Start ? e : e.AddDays(1);
            }
            else result.End = result.Start + (length ?? DefaultLength);
            return result;
        }

        private static void Mark(bool[] used, int from, int count)
        {
            for (int k = from; k < from + count && k < used.Length; k++) used[k] = true;
        }

        /// <summary>The token without the trailing punctuation a sentence adds.</summary>
        private static string Word(string token) => token.TrimEnd(',', ';', '!', '?');

        private static bool Same(string a, string b, CultureInfo culture) =>
            b.Length > 0 && string.Compare(a.TrimEnd('.'), b, culture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

        private static int? MatchPhrase(List<string> tokens, bool[] used, int i, string phrase, CultureInfo culture)
        {
            var parts = phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || i + parts.Length > tokens.Count) return null;
            for (int k = 0; k < parts.Length; k++)
                if (used[i + k] || !Same(Word(tokens[i + k]), parts[k], culture)) return null;
            return parts.Length;
        }

        private static DayOfWeek? Weekday(string word, CultureInfo culture)
        {
            var f = culture.DateTimeFormat;
            for (int d = 0; d < 7; d++)
            {
                if (Same(word, f.DayNames[d], culture)) return (DayOfWeek)d;
                // A two-letter abbreviation like German "So" is an everyday word, so only longer ones count.
                string abbr = f.AbbreviatedDayNames[d].TrimEnd('.');
                if (abbr.Length >= 3 && Same(word, abbr, culture)) return (DayOfWeek)d;
            }
            return null;
        }

        private static DateTime NextWeekday(DateTime today, DayOfWeek day) =>
            today.AddDays(((int)day - (int)today.DayOfWeek + 7) % 7);

        private static int? Month(string word, CultureInfo culture)
        {
            var f = culture.DateTimeFormat;
            for (int m = 0; m < 12; m++)
            {
                if (Same(word, f.MonthNames[m], culture) || Same(word, f.MonthGenitiveNames[m], culture)) return m + 1;
                string abbr = f.AbbreviatedMonthNames[m].TrimEnd('.');
                if (abbr.Length >= 3 && Same(word, abbr, culture)) return m + 1;
            }
            return null;
        }

        /// <summary>"nov 30", "30 november" and either one followed by a four-digit year.</summary>
        private static bool ReadMonthDate(List<string> tokens, int i, DateTime now, CultureInfo culture, out DateTime date, out int count)
        {
            date = default; count = 0;
            if (i + 1 >= tokens.Count) return false;
            int month = 0, day = 0;
            var first = DayNumber.Match(tokens[i]);
            var second = DayNumber.Match(tokens[i + 1]);
            if (Month(Word(tokens[i]), culture) is int m1 && second.Success)
            { month = m1; day = int.Parse(second.Groups["d"].Value, CultureInfo.InvariantCulture); }
            else if (first.Success && Month(Word(tokens[i + 1]), culture) is int m2)
            { month = m2; day = int.Parse(first.Groups["d"].Value, CultureInfo.InvariantCulture); }
            else return false;
            count = 2;
            int? year = null;
            if (i + 2 < tokens.Count && Year.Match(tokens[i + 2]) is { Success: true } y)
            { year = int.Parse(y.Groups["y"].Value, CultureInfo.InvariantCulture); count = 3; }
            return Resolve(year, month, day, now, out date);
        }

        private static bool ReadNumericDate(string token, DateTime now, CultureInfo culture, out DateTime date)
        {
            date = default;
            var m = NumericDate.Match(token);
            if (!m.Success) return false;
            int a = int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
            int b = int.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture);
            int? c = m.Groups["c"].Success ? int.Parse(m.Groups["c"].Value, CultureInfo.InvariantCulture) : null;
            int month, day;
            int? year;
            string order = DateOrder(culture);
            if (m.Groups["a"].Value.Length == 4)
            {
                if (c == null) return false;
                year = a; month = b; day = c.Value;
            }
            else if (order == "dmy") { day = a; month = b; year = c; }
            else { month = a; day = b; year = c; }
            if (year is int yy && yy < 100) year = 2000 + yy;
            return Resolve(year, month, day, now, out date);
        }

        private static string DateOrder(CultureInfo culture)
        {
            string p = culture.DateTimeFormat.ShortDatePattern.ToLowerInvariant();
            int d = p.IndexOf('d'), mo = p.IndexOf('m');
            return d >= 0 && d < mo ? "dmy" : "mdy";
        }

        /// <summary>A date with no year is the next one on or after today.</summary>
        private static bool Resolve(int? year, int month, int day, DateTime now, out DateTime date)
        {
            date = default;
            if (month < 1 || month > 12 || day < 1) return false;
            int y = year ?? now.Year;
            if (y < 1 || y > 9999 || day > DateTime.DaysInMonth(y, month)) return false;
            date = new DateTime(y, month, day);
            if (year == null && date < now.Date)
            {
                if (y + 1 > 9999 || day > DateTime.DaysInMonth(y + 1, month)) return false;
                date = new DateTime(y + 1, month, day);
            }
            return true;
        }

        /// <summary>A clock time. A bare number counts only beside am or pm, or with a colon,
        /// unless the caller is reading one side of a range.</summary>
        private static bool ReadClock(string token, bool requireMark, string? impliedMark, out TimeSpan time, out string mark)
        {
            time = default; mark = "";
            var m = Clock.Match(Word(token));
            if (!m.Success) return false;
            int h = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            int min = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            mark = m.Groups["s"].Value.ToLowerInvariant();
            if (requireMark && mark.Length == 0 && !m.Groups["m"].Success) return false;
            string use = mark.Length > 0 ? mark : impliedMark ?? "";
            if (min > 59) return false;
            if (use.Length > 0)
            {
                if (h < 1 || h > 12) return false;
                h = h % 12 + (use[0] == 'p' ? 12 : 0);
            }
            else if (h > 23) return false;
            time = new TimeSpan(h, min, 0);
            return true;
        }

        /// <summary>"3-5pm", "3pm-5:30pm", "15:00-16:15". A start with no am or pm takes the end's,
        /// unless that would put it after the end ("11-1pm" is 11 in the morning).</summary>
        private static bool ReadRange(string a, string b, out TimeSpan start, out TimeSpan end)
        {
            start = default;
            if (!ReadClock(b, false, null, out end, out string endMark)) return false;
            if (!ReadClock(a, false, null, out start, out string startMark)) return false;
            if (startMark.Length > 0 || endMark.Length == 0) return true;
            if (!ReadClock(a, false, endMark, out start, out _)) return false;
            if (start >= end && !ReadClock(a, false, endMark[0] == 'p' ? "am" : "pm", out start, out _)) return false;
            return true;
        }

        /// <summary>"2h", "90m", "1.5h", or "2 hours" across two tokens.</summary>
        private static bool ReadLength(List<string> tokens, int i, out TimeSpan length, out int count)
        {
            length = default; count = 0;
            var m = Length.Match(Word(tokens[i]));
            if (!m.Success) return false;
            string unit = m.Groups["u"].Value;
            count = 1;
            if (unit.Length == 0)
            {
                if (i + 1 >= tokens.Count || !Unit.IsMatch(Word(tokens[i + 1]))) return false;
                unit = Word(tokens[i + 1]); count = 2;
            }
            if (!double.TryParse(m.Groups["n"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n <= 0)
                return false;
            length = char.ToLowerInvariant(unit[0]) == 'h' ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
            return length <= TimeSpan.FromDays(14);
        }
    }
}
