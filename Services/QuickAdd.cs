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
    /// month names come from the culture, so only these need translating. Any of them may be more
    /// than one word, like Norwegian "i morgen".</summary>
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
    /// Words are split on spaces, so languages written without them read only the parts that
    /// have spaces around them.
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
            @"^(?<a>\d{1,4})(?<sep>[/.])(?<b>\d{1,2})(?:[/.](?<c>\d{2,4}))?\.?$", RegexOptions.CultureInvariant);
        private static readonly Regex DayNumber = new(@"^(?<d>\d{1,2})(?:\.|st|nd|rd|th)?,?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Year = new(@"^(?<y>\d{4}),?$", RegexOptions.CultureInvariant);
        private static readonly Regex Meridiem = new(@"^(am|pm|a\.m\.|p\.m\.)[,;]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex TimeLike = new(@"^\d{1,2}(?::\d{2})?(?:-\d{1,2}(?::\d{2})?)?$", RegexOptions.CultureInvariant);
        private static readonly Regex BareHour = new(@"^(?<h>\d{1,2})$", RegexOptions.CultureInvariant);

        public static QuickAddResult Parse(string text, DateTime now, CultureInfo culture, QuickAddWords words)
        {
            var tokens = JoinMeridiems((text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList());
            var used = new bool[tokens.Count];
            var result = new QuickAddResult();
            DateTime? date = null;
            bool dateIsWeekday = false;
            TimeSpan? start = null, end = null, length = null;
            bool startIsBareHour = false;
            bool allDay = false;
            var categories = new List<string>();
            List<string>? place = null;
            bool placeOpen = false;

            for (int i = 0; i < tokens.Count; i++)
            {
                if (used[i]) continue;
                string t = tokens[i];
                string w = Word(t);

                // Inside "at <place>", only a word that clearly means something else ends it, so
                // "at Sun Cafe" keeps Sun as part of the name.
                if (placeOpen)
                {
                    if (!EndsPlace(tokens, used, i, now, culture, words))
                    {
                        place!.Add(t);
                        used[i] = true;
                        continue;
                    }
                    placeOpen = false;
                }

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
                if (MatchPhrase(tokens, used, i, words.Today, culture) is int todayLength)
                { date = now.Date; dateIsWeekday = false; Mark(used, i, todayLength); continue; }
                if (MatchPhrase(tokens, used, i, words.Tomorrow, culture) is int tomorrowLength)
                { date = SafeDays(now.Date, 1); dateIsWeekday = false; Mark(used, i, tomorrowLength); continue; }
                if (MatchPhrase(tokens, used, i, words.Noon, culture) is int noonLength)
                { start = TimeSpan.FromHours(12); startIsBareHour = false; Mark(used, i, noonLength); continue; }
                if (MatchPhrase(tokens, used, i, words.For, culture) is int forLength &&
                    i + forLength < tokens.Count && ReadLength(tokens, i + forLength, out var len, out int lenTokens))
                {
                    length = len;
                    Mark(used, i, forLength + lenTokens);
                    continue;
                }
                if (MatchPhrase(tokens, used, i, words.Next, culture) is int nextLength &&
                    i + nextLength < tokens.Count && Weekday(Word(tokens[i + nextLength]), culture) is DayOfWeek nd)
                {
                    date = SafeDays(NextWeekday(now.Date, nd), 7);
                    dateIsWeekday = false;
                    Mark(used, i, nextLength + 1);
                    continue;
                }
                if (place == null && MatchPhrase(tokens, used, i, words.At, culture) is int atLength && i + atLength < tokens.Count)
                {
                    int j = i + atLength;
                    // "at 7" is a time, not a place.
                    if (BareHour.Match(Word(tokens[j])) is { Success: true } bh &&
                        int.Parse(bh.Groups["h"].Value, CultureInfo.InvariantCulture) is int hour && hour <= 23)
                    {
                        start = TimeSpan.FromHours(hour);
                        startIsBareHour = hour >= 1 && hour <= 11;
                        Mark(used, i, atLength + 1);
                        continue;
                    }
                    if (IsTimeOrDate(tokens, j, now, culture))
                    {
                        Mark(used, i, atLength);
                        continue;
                    }
                    place = new List<string>();
                    placeOpen = true;
                    Mark(used, i, atLength);
                    continue;
                }
                if (Weekday(w, culture) is DayOfWeek wd) { date = NextWeekday(now.Date, wd); dateIsWeekday = true; used[i] = true; continue; }
                if (ReadMonthDate(tokens, i, now, culture, out var md, out int mdTokens)) { date = md; dateIsWeekday = false; Mark(used, i, mdTokens); continue; }
                if (ReadNumericDate(t, now, culture, out var numeric)) { date = numeric; dateIsWeekday = false; used[i] = true; continue; }
                var range = Range.Match(w);
                if (range.Success && ReadRange(range.Groups["a"].Value, range.Groups["b"].Value, out var rs, out var re))
                {
                    start = rs; end = re; startIsBareHour = false; used[i] = true;
                    continue;
                }
                if (ReadClock(t, requireMark: true, null, out var clock, out _)) { start = clock; startIsBareHour = false; used[i] = true; continue; }
            }

            if (place != null) result.Location = string.Join(" ", place).Trim().TrimEnd(',');
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
                var nextHour = SafeAdd(now.Date, TimeSpan.FromHours(now.Hour + 1));
                result.Start = date == null ? nextHour : SafeAdd(date.Value, nextHour.TimeOfDay);
            }
            else
            {
                var day = date ?? now.Date;
                var at = SafeAdd(day, start.Value);
                // "dinner at 7" at ten in the morning means seven tonight.
                if (startIsBareHour && at <= now && SafeAdd(at, TimeSpan.FromHours(12)) > now)
                {
                    start = start.Value + TimeSpan.FromHours(12);
                    at = SafeAdd(day, start.Value);
                }
                if (at <= now && date == null) at = SafeAdd(SafeDays(day, 1), start.Value);
                // "standup wed 9am" said on a Wednesday afternoon means next Wednesday.
                else if (at <= now && dateIsWeekday) at = SafeAdd(SafeDays(day, 7), start.Value);
                result.Start = at;
            }

            if (end != null)
            {
                var e = SafeAdd(result.Start.Date, end.Value);
                result.End = e > result.Start ? e : SafeDays(e, 1);
            }
            else result.End = SafeAdd(result.Start, length ?? DefaultLength);
            if (result.End < result.Start) result.End = result.Start;
            return result;
        }

        /// <summary>Joins "7:30 pm" and "3 pm" into one token, so the clock reads them together.</summary>
        private static List<string> JoinMeridiems(List<string> tokens)
        {
            var joined = new List<string>(tokens.Count);
            foreach (var t in tokens)
            {
                if (joined.Count > 0 && Meridiem.IsMatch(t) && TimeLike.IsMatch(joined[joined.Count - 1]))
                {
                    string mark = t.TrimEnd(',', ';').Replace(".", "");
                    joined[joined.Count - 1] += mark;
                    continue;
                }
                joined.Add(t);
            }
            return joined;
        }

        /// <summary>The word ends an "at" place: a time, a range, a date, a #category or one of
        /// the keywords. A weekday counts only last or right before a time, so it can still be
        /// part of a name like "Sun Cafe".</summary>
        private static bool EndsPlace(List<string> tokens, bool[] used, int i, DateTime now, CultureInfo culture, QuickAddWords words)
        {
            string t = tokens[i];
            string w = Word(t);
            if (t.Length > 1 && t[0] == '#') return true;
            foreach (var phrase in new[] { words.AllDay, words.Today, words.Tomorrow, words.Noon })
                if (MatchPhrase(tokens, used, i, phrase, culture) != null) return true;
            if (MatchPhrase(tokens, used, i, words.For, culture) is int f && i + f < tokens.Count && ReadLength(tokens, i + f, out _, out _))
                return true;
            if (MatchPhrase(tokens, used, i, words.Next, culture) is int n && i + n < tokens.Count && Weekday(Word(tokens[i + n]), culture) != null)
                return true;
            if (Weekday(w, culture) != null)
                return i + 1 >= tokens.Count || IsClockOrRange(tokens[i + 1]);
            return IsTimeOrDate(tokens, i, now, culture);
        }

        private static bool IsTimeOrDate(List<string> tokens, int i, DateTime now, CultureInfo culture)
        {
            if (IsClockOrRange(tokens[i])) return true;
            if (ReadMonthDate(tokens, i, now, culture, out _, out _)) return true;
            return ReadNumericDate(tokens[i], now, culture, out _);
        }

        private static bool IsClockOrRange(string token)
        {
            string w = Word(token);
            var range = Range.Match(w);
            if (range.Success && ReadRange(range.Groups["a"].Value, range.Groups["b"].Value, out _, out _)) return true;
            return ReadClock(token, requireMark: true, null, out _, out _);
        }

        private static DateTime SafeAdd(DateTime at, TimeSpan by) =>
            by > TimeSpan.Zero && DateTime.MaxValue - at < by ? DateTime.MaxValue : at + by;

        private static DateTime SafeDays(DateTime at, int days) => SafeAdd(at, TimeSpan.FromDays(days));

        private static void Mark(bool[] used, int from, int count)
        {
            for (int k = from; k < from + count && k < used.Length; k++) used[k] = true;
        }

        /// <summary>The token without the trailing punctuation a sentence adds.</summary>
        private static string Word(string token) => token.TrimEnd(',', ';', '!', '?');

        private static bool Same(string a, string b, CultureInfo culture) =>
            b.Length > 0 && string.Compare(a.TrimEnd('.'), b.TrimEnd('.'), culture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

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
            SafeDays(today, ((int)day - (int)today.DayOfWeek + 7) % 7);

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

        /// <summary>"12/2", "2.12.2026", "2026/12/02". A dot with only two parts reads as a date
        /// only where the culture writes dates with dots, so "3.5" stays a number in English.</summary>
        private static bool ReadNumericDate(string token, DateTime now, CultureInfo culture, out DateTime date)
        {
            date = default;
            var m = NumericDate.Match(token);
            if (!m.Success) return false;
            if (m.Groups["sep"].Value == "." && !m.Groups["c"].Success &&
                !culture.DateTimeFormat.DateSeparator.Contains("."))
                return false;
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

        /// <summary>A date with no year is the next one on or after today, so "feb 29" in a
        /// common year lands on the next leap year.</summary>
        private static bool Resolve(int? year, int month, int day, DateTime now, out DateTime date)
        {
            date = default;
            if (month < 1 || month > 12 || day < 1 || day > 31) return false;
            if (year is int fixedYear)
            {
                if (fixedYear < 1 || fixedYear > 9999 || day > DateTime.DaysInMonth(fixedYear, month)) return false;
                date = new DateTime(fixedYear, month, day);
                return true;
            }
            for (int y = now.Year; y <= Math.Min(9999, now.Year + 8); y++)
            {
                if (day > DateTime.DaysInMonth(y, month)) continue;
                var candidate = new DateTime(y, month, day);
                if (candidate < now.Date) continue;
                date = candidate;
                return true;
            }
            return false;
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

        /// <summary>"3-5pm", "2pm-3", "3pm-5:30pm", "15:00-16:15". One side needs am, pm or a
        /// colon, so "Chapter 1-2" stays text. A side with no am or pm takes the other's, unless
        /// that would put the start after the end ("11-1pm" is 11 in the morning).</summary>
        private static bool ReadRange(string a, string b, out TimeSpan start, out TimeSpan end)
        {
            start = default; end = default;
            if (!ReadClock(b, false, null, out end, out string endMark)) return false;
            if (!ReadClock(a, false, null, out start, out string startMark)) return false;
            if (startMark.Length == 0 && endMark.Length == 0 && !a.Contains(":") && !b.Contains(":")) return false;
            if (startMark.Length > 0 && endMark.Length == 0)
            {
                if (!ReadClock(b, false, startMark, out end, out _)) return false;
                if (end <= start && !ReadClock(b, false, startMark[0] == 'p' ? "am" : "pm", out end, out _)) return false;
                return true;
            }
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
