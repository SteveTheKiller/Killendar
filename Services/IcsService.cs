using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Killendar.Models;
using TimeZoneConverter;

namespace Killendar.Services
{
    /// <summary>
    /// What a parse produced, INCLUDING what it could not keep. Every counter here exists because
    /// an appointment that vanishes silently is worse than one that never imported: the user only
    /// finds out by missing it. Import reports all of them.
    /// </summary>
    public sealed class IcsParseResult
    {
        /// <summary>The appointments Killendar can actually store.</summary>
        public List<CalendarEvent> Events = [];

        /// <summary>VEVENTs dropped for having no readable start date.</summary>
        public int Unreadable;

        /// <summary>VTODO / VJOURNAL / VFREEBUSY blocks. Killendar has nowhere to put a task,
        /// a journal entry or a free/busy block. Also includes unsupported future-range edits.
        /// These components are dropped and counted.</summary>
        public int Unsupported;

        /// <summary>The subset of unsupported components carrying future-range edits.</summary>
        public int UnsupportedRanges;

        /// <summary>VEVENTs carrying RRULE or RDATE - a repeating appointment. Killendar has no
        /// repeat concept, so ONLY THE FIRST OCCURRENCE was imported and every later one is lost.
        /// Counted so the import can say so out loud. Remove this when repeats are modeled.</summary>
        public int Repeating;
    }

    /// <summary>
    /// Minimal RFC-5545 iCalendar parser and writer.
    /// Handles VCALENDAR/VEVENT import and export without external dependencies.
    /// </summary>
    public static class IcsService
    {
        // ── Import ────────────────────────────────────────────────────────────

        public static IcsParseResult ParseFile(string path)
        {
            var lines = UnfoldLines(File.ReadAllText(path, Encoding.UTF8));
            return ParseLines(lines);
        }

        public static IcsParseResult ParseText(string icsText)
        {
            var lines = UnfoldLines(icsText);
            return ParseLines(lines);
        }

        private static List<string> UnfoldLines(string raw)
        {
            // RFC-5545 line unfolding: CRLF + space/tab = continuation
            raw = raw.Replace("\r\n", "\n").Replace("\r", "\n");
            var sb     = new StringBuilder();
            var result = new List<string>();

            foreach (var line in raw.Split('\n'))
            {
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                {
                    sb.Append(line.Substring(1));
                }
                else
                {
                    if (sb.Length > 0) result.Add(sb.ToString());
                    sb.Clear();
                    sb.Append(line);
                }
            }
            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        private static IcsParseResult ParseLines(List<string> lines)
        {
            var result = new IcsParseResult();

            // Open components, innermost last. A property belongs to the component it sits
            // directly inside, and nothing else - see the VALARM note below.
            var stack = new List<string>();

            CalendarEvent? current = null;
            bool haveStart = false;   // no DTSTART means no appointment, however much else parsed
            bool repeating = false;
            bool unsupportedRange = false;
            string? duration = null;
            DateTime? recurrenceId = null;   // set when this VEVENT replaces one date of a series
            bool recurrenceAllDay = false;
            DateTime? recurrenceAnchor = null;
            OccurrenceTimeBasis recurrenceBasis = OccurrenceTimeBasis.Date;
            string recurrenceZone = "";
            var exclusions = new List<(DateTime dt, bool allDay, RecurrenceTimeBasis basis, DateTime? anchor, string zone)>();

            foreach (var raw in lines)
            {
                var (name, param, value) = SplitProperty(raw);
                string upper = name.ToUpperInvariant();

                if (upper == "BEGIN")
                {
                    string comp = value.Trim().ToUpperInvariant();
                    stack.Add(comp);

                    if (comp == "VEVENT" && current == null)
                    {
                        current      = new CalendarEvent();
                        haveStart    = false;
                        repeating    = false;
                        unsupportedRange = false;
                        duration     = null;
                        recurrenceId = null;
                        recurrenceAllDay = false;
                        recurrenceAnchor = null;
                        recurrenceZone = "";
                        exclusions.Clear();
                    }
                    else if (current == null &&
                             (comp == "VTODO" || comp == "VJOURNAL" || comp == "VFREEBUSY"))
                    {
                        result.Unsupported++;
                    }
                    continue;
                }

                if (upper == "END")
                {
                    string comp = value.Trim().ToUpperInvariant();
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);

                    if (comp == "VEVENT" && current != null)
                    {
                        if (unsupportedRange)
                        {
                            result.Unsupported++;
                            result.UnsupportedRanges++;
                        }
                        else if (!haveStart)
                        {
                            result.Unreadable++;
                        }
                        else
                        {
                            if (current.End == default && duration != null)
                            {
                                var span = ParseDuration(duration);
                                if (span == null)
                                {
                                    result.Unreadable++;
                                    current = null;
                                    continue;
                                }
                                try
                                {
                                    if (!current.AllDay && span.Value.days > 0)
                                    {
                                        current.DurationDays = span.Value.days;
                                        current.DurationTimeTicks = span.Value.timeTicks;
                                        current.End = NominalDuration.EndAfterStartChange(current, current.Start);
                                    }
                                    else current.End = Recurrence.UsesTimeBasis(current)
                                        ? (current.Start.ToUniversalTime() + span.Value.span).ToLocalTime()
                                        : current.Start + span.Value.span;
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    result.Unreadable++;
                                    current = null;
                                    continue;
                                }
                            }
                            if (Recurrence.UsesTimeBasis(current)
                                ? current.End.ToUniversalTime() <= current.Start.ToUniversalTime()
                                : current.End <= current.Start)
                            {
                                try
                                {
                                    current.End = current.AllDay
                                        ? current.Start.AddDays(1)
                                        : Recurrence.UsesTimeBasis(current)
                                            ? current.Start.ToUniversalTime().AddHours(1).ToLocalTime()
                                            : current.Start.AddHours(1);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    result.Unreadable++;
                                    current = null;
                                    continue;
                                }
                            }

                            foreach (var exclusion in exclusions)
                            {
                                if (exclusion.allDay) current.SkipDates.Add(exclusion.dt.Date);
                                else if (current.AllDay) repeating = true;
                                else current.SkipOccurrences.Add(CanonicalExclusion(current, exclusion));
                            }
                            if (current.RepeatUntil.HasValue &&
                                (current.AllDay == current.RepeatUntilExact.HasValue ||
                                 (!current.AllDay && current.RepeatUntilExact.HasValue &&
                                  (current.RepeatTimeBasis == RecurrenceTimeBasis.Local) == (current.RepeatUntilExact.Value.Kind == DateTimeKind.Utc))))
                            {
                                current.Repeat = RepeatFreq.None;
                                repeating = true;
                            }

                            // Now that UID is final, an override can be pointed at its series. The
                            // row identity combines that series with the original occurrence,
                            // so repeated imports skip the same override even when it was moved.
                            if (recurrenceId.HasValue)
                            {
                                current.SeriesId        = current.Id;
                                current.OccurrenceStart = recurrenceId.Value;
                                current.OccurrenceAnchor = recurrenceAnchor;
                                current.OccurrenceBasis = recurrenceBasis;
                                current.OccurrenceZoneId = recurrenceZone;
                                long occurrenceTicks = recurrenceAllDay
                                    ? recurrenceId.Value.Date.Ticks
                                    : recurrenceId.Value.ToUniversalTime().Ticks;
                                current.Id = IdFromUid("override:" + current.SeriesId.Value.ToString("N") +
                                    (recurrenceAllDay ? ":date:" : ":utc:") +
                                    occurrenceTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                                current.Repeat          = RepeatFreq.None;
                            }

                            if (repeating) result.Repeating++;
                            result.Events.Add(current);
                        }
                        current = null;
                    }
                    continue;
                }

                if (current == null) continue;

                // A property counts only when VEVENT is the component we are DIRECTLY inside.
                // A VALARM nested in a VEVENT carries its own DESCRIPTION, DURATION and ATTENDEE:
                // without this guard the reminder's text overwrote the appointment's description
                // and the reminder's address was added to the appointment's attendee list.
                if (stack.Count == 0 || stack[stack.Count - 1] != "VEVENT") continue;

                switch (upper)
                {
                    case "UID":
                        if (value.Length > 0) current.Id = IdFromUid(value);
                        break;

                    case "SUMMARY":
                        current.Title = UnescapeText(value);
                        break;

                    case "DESCRIPTION":
                        current.Description = UnescapeText(value);
                        break;

                    case "LOCATION":
                        current.Location = UnescapeText(value);
                        break;

                    case "CATEGORIES":
                        current.Categories = ParseCategoryList(value);
                        break;

                    // A repeat rule Killendar can hold is read into the appointment. One it cannot
                    // - "the third Monday of the month", an hourly repeat - still imports as a
                    // single date, and the loss is COUNTED so the import can say so rather than
                    // hiding it.
                    case "RRULE":
                        if (!ParseRRule(value, current)) repeating = true;
                        break;

                    // A loose list of extra dates is not a pattern; there is nowhere to put it.
                    case "RDATE":
                        repeating = true;
                        break;

                    case "EXDATE":
                        foreach (var part in value.Split(','))
                        {
                            var ex = ParseDateTime(part, param);
                            if (ex != null) exclusions.Add(ex.Value);
                        }
                        break;

                    // This VEVENT replaces ONE date of a series. Only NOTED here and applied at
                    // END:VEVENT, because the link back to the series is the UID and RFC 5545
                    // does not require UID to come first - resolving it now would attach the
                    // override to whatever id the event happened to have at this point.
                    case "RECURRENCE-ID":
                    {
                        foreach (string item in param.Split(';'))
                        {
                            int equals = item.IndexOf('=');
                            if (equals < 0) continue;
                            if (item.Substring(0, equals).Trim().Equals("RANGE", StringComparison.OrdinalIgnoreCase) &&
                                item.Substring(equals + 1).Trim().Trim('"').Equals("THISANDFUTURE", StringComparison.OrdinalIgnoreCase))
                                unsupportedRange = true;
                        }
                        if (unsupportedRange) break;
                        var rid = ParseDateTime(value, param);
                        if (rid != null)
                        {
                            recurrenceId = rid.Value.dt;
                            recurrenceAllDay = rid.Value.allDay;
                            recurrenceAnchor = rid.Value.anchor ?? DateTime.SpecifyKind(rid.Value.dt, DateTimeKind.Unspecified);
                            recurrenceBasis = rid.Value.allDay ? OccurrenceTimeBasis.Date : rid.Value.basis == RecurrenceTimeBasis.Utc
                                ? OccurrenceTimeBasis.Utc : rid.Value.basis == RecurrenceTimeBasis.SourceZone
                                    ? OccurrenceTimeBasis.SourceZone : OccurrenceTimeBasis.Floating;
                            recurrenceZone = rid.Value.zone;
                        }
                        break;
                    }

                    case "DTSTART":
                    {
                        var parsed = ParseDateTime(value, param);
                        if (parsed == null) break;      // haveStart stays false -> counted, not guessed
                        current.Start  = parsed.Value.dt;
                        current.AllDay = parsed.Value.allDay;
                        current.RepeatTimeBasis = parsed.Value.basis;
                        current.RepeatAnchor = parsed.Value.anchor;
                        current.RepeatZoneId = parsed.Value.zone;
                        haveStart      = true;
                        break;
                    }

                    case "DTEND":
                    case "DUE":
                    {
                        var parsed = ParseDateTime(value, param);
                        if (parsed != null) current.End = parsed.Value.dt;
                        break;
                    }

                    case "DURATION":
                        duration = value;
                        break;

                    case "CREATED":
                    case "DTSTAMP":
                    {
                        var parsed = ParseDateTime(value, param);
                        if (parsed != null) current.Created = parsed.Value.dt;
                        break;
                    }

                    case "LAST-MODIFIED":
                    {
                        var parsed = ParseDateTime(value, param);
                        if (parsed != null) current.Modified = parsed.Value.dt;
                        break;
                    }

                    case "ATTENDEE":
                    {
                        // Value is "mailto:user@example.com" or raw email.
                        // Also check CN= parameter for display name (ignored for now).
                        string addr = value.Trim();
                        if (addr.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                            addr = addr.Substring(7);
                        if (!string.IsNullOrWhiteSpace(addr) && addr.Contains("@"))
                            current.Attendees.Add(addr);
                        break;
                    }

                    case "ORGANIZER":
                        // Could store organizer; for now skip
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Reads an RRULE into the appointment. Returns FALSE when the rule says something
        /// Killendar cannot hold, in which case nothing is written and the caller counts the loss.
        ///
        /// Supported: FREQ of DAILY / WEEKLY / MONTHLY / YEARLY, INTERVAL, COUNT, UNTIL, and
        /// BYDAY as plain weekday names. Deliberately NOT supported: an ordinal BYDAY ("3MO" =
        /// the third Monday), BYMONTHDAY, BYSETPOS, BYWEEKNO and the rest. Those are real rules
        /// that Killendar has no way to draw, and half-importing one would put the appointment on
        /// the wrong days - worse than importing it once and saying so.
        /// </summary>
        private static bool ParseRRule(string value, CalendarEvent ev)
        {
            var freq     = RepeatFreq.None;
            int interval = 1;
            int count    = 0;
            var weekStart = DayOfWeek.Monday;
            DateTime? until = null;
            DateTime? untilExact = null;
            var days = new List<DayOfWeek>();

            foreach (var part in value.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;

                string key = part.Substring(0, eq).Trim().ToUpperInvariant();
                string val = part.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "FREQ":
                        switch (val.ToUpperInvariant())
                        {
                            case "DAILY":   freq = RepeatFreq.Daily;   break;
                            case "WEEKLY":  freq = RepeatFreq.Weekly;  break;
                            case "MONTHLY": freq = RepeatFreq.Monthly; break;
                            case "YEARLY":  freq = RepeatFreq.Yearly;  break;
                            default: return false;   // HOURLY, MINUTELY, SECONDLY
                        }
                        break;

                    case "INTERVAL":
                        if (!int.TryParse(val, out interval) || interval < 1) return false;
                        break;

                    case "COUNT":
                        if (!int.TryParse(val, out count) || count < 1) return false;
                        break;

                    case "UNTIL":
                    {
                        var u = ParseDateTime(val, "");
                        if (u == null) return false;
                        until = u.Value.dt.Date;
                        if (!u.Value.allDay)
                            untilExact = u.Value.basis == RecurrenceTimeBasis.Utc
                                ? DateTime.SpecifyKind(u.Value.anchor!.Value, DateTimeKind.Utc)
                                : DateTime.SpecifyKind(u.Value.dt, DateTimeKind.Unspecified);
                        break;
                    }

                    case "BYDAY":
                        foreach (var d in val.Split(','))
                        {
                            var day = ParseWeekday(d.Trim());
                            if (day == null) return false;   // ordinal form, e.g. "3MO" or "-1FR"
                            if (!days.Contains(day.Value)) days.Add(day.Value);
                        }
                        break;

                    case "WKST":
                        var parsedWeekStart = ParseWeekday(val);
                        if (!parsedWeekStart.HasValue) return false;
                        weekStart = parsedWeekStart.Value;
                        break;

                    default:
                        return false;   // BYMONTHDAY, BYSETPOS, BYMONTH, BYWEEKNO, ...
                }
            }

            if (freq == RepeatFreq.None) return false;

            // BYDAY on anything but a weekly rule means "the Mondays within each month/year",
            // which is an ordinal rule in disguise.
            if (days.Count > 0 && freq != RepeatFreq.Weekly) return false;

            ev.Repeat         = freq;
            ev.RepeatInterval = interval;
            ev.RepeatCount    = count;
            ev.RepeatWeekStart = weekStart;
            ev.RepeatUntil    = until;
            ev.RepeatUntilExact = untilExact;
            ev.RepeatDays     = days;
            return true;
        }

        private static DateTime CanonicalExclusion(CalendarEvent master,
            (DateTime dt, bool allDay, RecurrenceTimeBasis basis, DateTime? anchor, string zone) exclusion)
        {
            if ((master.RepeatTimeBasis == RecurrenceTimeBasis.Utc && exclusion.basis == RecurrenceTimeBasis.Utc) ||
                (master.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone && exclusion.basis == RecurrenceTimeBasis.SourceZone && master.RepeatZoneId == exclusion.zone))
                return exclusion.anchor!.Value;
            if (!Recurrence.UsesTimeBasis(master)) return DateTime.SpecifyKind(exclusion.dt, DateTimeKind.Unspecified);
            try { return Recurrence.OriginalTimeBasisStart(master, exclusion.dt); }
            catch (InvalidOperationException) { return Recurrence.ToTimeBasis(master, exclusion.dt); }
        }

        /// <summary>"MO" through "SU", or null for the ordinal form ("3MO") this cannot draw.</summary>
        private static DayOfWeek? ParseWeekday(string s)
        {
            return s.ToUpperInvariant() switch
            {
                "SU" => (DayOfWeek?)DayOfWeek.Sunday,
                "MO" => (DayOfWeek?)DayOfWeek.Monday,
                "TU" => (DayOfWeek?)DayOfWeek.Tuesday,
                "WE" => (DayOfWeek?)DayOfWeek.Wednesday,
                "TH" => (DayOfWeek?)DayOfWeek.Thursday,
                "FR" => (DayOfWeek?)DayOfWeek.Friday,
                "SA" => (DayOfWeek?)DayOfWeek.Saturday,
                _ => null,
            };
        }

        private static string WeekdayCode(DayOfWeek d)
        {
            return d switch
            {
                DayOfWeek.Sunday => "SU",
                DayOfWeek.Monday => "MO",
                DayOfWeek.Tuesday => "TU",
                DayOfWeek.Wednesday => "WE",
                DayOfWeek.Thursday => "TH",
                DayOfWeek.Friday => "FR",
                _ => "SA",
            };
        }

        private static Guid IdFromUid(string uid)
        {
            if (Guid.TryParse(uid, out var id)) return id;
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("Killendar ICS UID:" + uid));
            var bytes = new byte[16];
            Array.Copy(hash, bytes, bytes.Length);
            return new Guid(bytes);
        }

        private static (string name, string param, string value) SplitProperty(string line)
        {
            // Split NAME;PARAM=val:VALUE
            int colon = line.IndexOf(':');
            if (colon < 0) return (line, "", "");

            var left  = line.Substring(0, colon);
            var value = line.Substring(colon + 1);

            int semi = left.IndexOf(';');
            if (semi < 0) return (left, "", value);

            return (left.Substring(0, semi), left.Substring(semi + 1), value);
        }

        /// <summary>
        /// Null when the value is not a date this parser can read.
        ///
        /// It used to return DateTime.Now on failure, which meant a malformed or unusual file
        /// quietly dropped appointments onto TODAY at whatever time the import happened to run,
        /// and nothing anywhere told the user. A date that cannot be read is now a failure the
        /// caller has to deal with, not a guess dressed up as data.
        /// </summary>
        private static (DateTime dt, bool allDay, RecurrenceTimeBasis basis, DateTime? anchor, string zone)? ParseDateTime(string value, string param)
        {
            value = value.Trim();
            if (value.Length == 0) return null;

            // DATE-only format: YYYYMMDD
            if (value.Length == 8 && !value.Contains('T'))
            {
                if (TryParseDate(value, out var d))
                    return (d, true, RecurrenceTimeBasis.Local, null, "");
                return null;
            }

            // DATE-TIME: YYYYMMDDTHHMMSS[Z]
            if (value.Length >= 15 && value.Contains('T'))
            {
                bool utc = value.EndsWith("Z");
                var s    = value.TrimEnd('Z');
                if (s.Length >= 15 &&
                    int.TryParse(s.Substring(0,  4), out int yr)  &&
                    int.TryParse(s.Substring(4,  2), out int mo)  &&
                    int.TryParse(s.Substring(6,  2), out int dy)  &&
                    int.TryParse(s.Substring(9,  2), out int hr)  &&
                    int.TryParse(s.Substring(11, 2), out int mn)  &&
                    int.TryParse(s.Substring(13, 2), out int sc))
                {
                    // Each field parsed as a number and is still nonsense as a date (month 13,
                    // 31 February, hour 25). The ctor throws; that is a bad line, not a crash.
                    try
                    {
                        string? zoneId = null;
                        foreach (string item in param.Split(';'))
                            if (item.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))
                                zoneId = item.Substring(5).Trim().Trim('"');

                        var kind = utc ? DateTimeKind.Utc : zoneId == null
                            ? DateTimeKind.Local : DateTimeKind.Unspecified;
                        var dt   = new DateTime(yr, mo, dy, hr, mn, sc, kind);
                        var anchor = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
                        if (utc) dt = dt.ToLocalTime();
                        else if (zoneId != null)
                        {
                            TimeZoneInfo zone;
                            try { zone = TZConvert.GetTimeZoneInfo(zoneId); }
                            catch (TimeZoneNotFoundException ex)
                            {
                                throw new TimeZoneNotFoundException(
                                    "Unsupported calendar time zone: " + zoneId, ex);
                            }
                            if (zone.IsAmbiguousTime(dt))
                            {
                                // The larger offset gives the first UTC occurrence of a repeated hour.
                                var offsets = zone.GetAmbiguousTimeOffsets(dt);
                                var offset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
                                dt = DateTime.SpecifyKind(dt - offset, DateTimeKind.Utc).ToLocalTime();
                            }
                            else
                            {
                                // A time the clocks skipped over has no direct conversion. RFC 5545
                                // reads it with the offset in force just before the gap.
                                dt = zone.IsInvalidTime(dt)
                                    ? DateTime.SpecifyKind(dt - zone.GetUtcOffset(dt.AddHours(-12)),
                                        DateTimeKind.Utc).ToLocalTime()
                                    : TimeZoneInfo.ConvertTimeToUtc(dt, zone).ToLocalTime();
                            }
                        }
                        return (dt, false, utc ? RecurrenceTimeBasis.Utc : zoneId == null
                            ? RecurrenceTimeBasis.Local : RecurrenceTimeBasis.SourceZone,
                            utc || zoneId != null ? anchor : (DateTime?)null, zoneId ?? "");
                    }
                    catch (ArgumentOutOfRangeException) { return null; }
                }
            }

            return null;
        }

        private static bool TryParseDate(string s, out DateTime result)
        {
            result = default;
            if (s.Length < 8) return false;
            if (int.TryParse(s.Substring(0, 4), out int yr) &&
                int.TryParse(s.Substring(4, 2), out int mo) &&
                int.TryParse(s.Substring(6, 2), out int dy))
            {
                try
                {
                    result = new DateTime(yr, mo, dy, 0, 0, 0, DateTimeKind.Local);
                    return true;
                }
                catch (ArgumentOutOfRangeException) { return false; }
            }
            return false;
        }

        private static (TimeSpan span, int days, long timeTicks)? ParseDuration(string value)
        {
            // Basic DURATION: P[nW][nD][T[nH][nM][nS]]
            var m = Regex.Match(value,
                @"^(?<neg>-)?P(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<mn>\d+)M)?(?:(?<s>\d+)S)?)?$",
                RegexOptions.IgnoreCase);
            if (!m.Success) return null;

            try
            {
                int weeks   = m.Groups["w"].Success  ? int.Parse(m.Groups["w"].Value)  : 0;
                int days    = m.Groups["d"].Success  ? int.Parse(m.Groups["d"].Value)  : 0;
                int hours   = m.Groups["h"].Success  ? int.Parse(m.Groups["h"].Value)  : 0;
                int minutes = m.Groups["mn"].Success ? int.Parse(m.Groups["mn"].Value) : 0;
                int seconds = m.Groups["s"].Success  ? int.Parse(m.Groups["s"].Value)  : 0;

                int nominalDays = checked(weeks * 7 + days);
                var time = new TimeSpan(hours, minutes, seconds);
                var ts = new TimeSpan(nominalDays, 0, 0, 0) + time;
                return m.Groups["neg"].Success ? (ts.Negate(), 0, 0L) : (ts, nominalDays, time.Ticks);
            }
            catch (OverflowException) { return null; }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        private static string UnescapeText(string s)
        {
            var decoded = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if (next == 'n' || next == 'N')
                    {
                        decoded.Append('\n');
                        i++;
                        continue;
                    }
                    if (next == '\\' || next == ',' || next == ';')
                    {
                        decoded.Append(next);
                        i++;
                        continue;
                    }
                }
                decoded.Append(c);
            }
            return decoded.ToString();
        }

        /// <summary>
        /// Splits an RFC-5545 CATEGORIES value. Unlike every other text property, this one is a
        /// comma-separated LIST, so the split has to happen on UNESCAPED commas only: a comma
        /// inside a single category name arrives as "\," and must not break the list. Each part
        /// is unescaped after the split, never before, or the two cases become indistinguishable.
        ///
        /// Killendar's own storage uses commas as its separator too, so a name that survives the
        /// split still containing one has it stripped rather than being allowed to split again
        /// downstream - the same rule the Add and Rename boxes apply.
        /// </summary>
        private static string ParseCategoryList(string value)
        {
            var names = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)   // keep the escape pair intact for the unescape below
                {
                    sb.Append(c).Append(value[i + 1]);
                    i++;
                    continue;
                }
                if (c == ',')
                {
                    names.Add(UnescapeText(sb.ToString()));
                    sb.Clear();
                    continue;
                }
                sb.Append(c);
            }
            names.Add(UnescapeText(sb.ToString()));

            var cleaned = new List<string>();
            foreach (string n in names)
            {
                string t = n.Replace(",", "").Trim();
                if (t.Length > 0) cleaned.Add(t);
            }
            return EventStore.NormalizeCategories(string.Join(", ", cleaned));
        }

        // ── Export ────────────────────────────────────────────────────────────

        public static void ExportToFile(IEnumerable<CalendarEvent> events, string path)
        {
            File.WriteAllText(path, BuildIcs(events), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static string BuildIcs(IEnumerable<CalendarEvent> events)
        {
            var rows = new List<CalendarEvent>(events);
            var masters = new Dictionary<Guid, CalendarEvent>();
            foreach (var row in rows) if (row.IsSeries) masters[row.Id] = row;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!row.IsOverride || row.OccurrenceAnchor.HasValue) continue;
                if (!row.SeriesId.HasValue || !masters.TryGetValue(row.SeriesId.Value, out var master) ||
                    !row.OccurrenceStart.HasValue)
                    throw new NotSupportedException("The original recurrence identity of this appointment cannot be recovered for export.");
                row = row.Clone();
                Recurrence.SetOccurrenceIdentity(row, master, Recurrence.CanonicalOccurrenceStart(master, row));
                rows[i] = row;
            }
            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//killertools.net//Killendar//EN");
            sb.AppendLine("CALSCALE:GREGORIAN");
            sb.AppendLine("METHOD:PUBLISH");

            var zones = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ev in rows)
            {
                var used = new List<string>();
                if (ev.IsOverride && ev.OccurrenceBasis == OccurrenceTimeBasis.SourceZone) used.Add(ev.OccurrenceZoneId);
                if ((ev.IsSeries || NominalDuration.HasValue(ev)) && Recurrence.UsesTimeBasis(ev) &&
                    ev.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone) used.Add(ev.RepeatZoneId);
                foreach (string zone in used)
                {
                    if (zone.Length == 0 || !zones.Add(zone)) continue;
                    string definition = IcsTimeZoneWriter.Build(zone, TZConvert.GetTimeZoneInfo(zone));
                    foreach (string line in definition.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        sb.AppendLine(FoldLine(line));
                }
            }

            foreach (var ev in rows)
            {
                sb.AppendLine("BEGIN:VEVENT");

                // The UID names the SERIES. A row that replaces one date carries its master's UID
                // plus a RECURRENCE-ID saying which date - that pairing is how every other
                // calendar links the two, and writing the override's own id instead would arrive
                // at the other end as an unrelated appointment sitting on top of the series.
                sb.AppendLine($"UID:{ev.SeriesKey}");
                sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}");
                sb.AppendLine($"CREATED:{ev.Created.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");
                sb.AppendLine($"LAST-MODIFIED:{ev.Modified.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");

                if (ev.AllDay)
                {
                    sb.AppendLine($"DTSTART;VALUE=DATE:{ev.Start:yyyyMMdd}");
                    sb.AppendLine($"DTEND;VALUE=DATE:{ev.End:yyyyMMdd}");
                }
                else if ((ev.IsSeries || NominalDuration.HasValue(ev)) && Recurrence.UsesTimeBasis(ev))
                {
                    var duration = ev.End.ToUniversalTime() - ev.Start.ToUniversalTime();
                    if (duration <= TimeSpan.Zero) duration = TimeSpan.FromHours(1);
                    var nominalStart = ev.IsSeries ? ev.RepeatAnchor!.Value : NominalDuration.SourceStart(ev, ev.Start);
                    var startUtc = Recurrence.FromTimeBasis(ev, nominalStart);
                    if (NominalDuration.HasValue(ev) && startUtc != ev.Start.ToUniversalTime())
                        throw new NotSupportedException("This source-zone start cannot represent the appointment's exact instant for export.");
                    var endUtc = startUtc + duration;
                    if (ev.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone)
                    {
                        sb.AppendLine(FoldLine($"DTSTART;TZID=\"{ev.RepeatZoneId}\":{nominalStart:yyyyMMdd'T'HHmmss}"));
                        if (NominalDuration.HasValue(ev)) sb.AppendLine("DURATION:" + NominalDuration.Format(ev));
                        else
                        {
                            if (duration.Ticks % TimeSpan.TicksPerSecond != 0)
                                throw new NotSupportedException("A calendar recurrence duration with fractional seconds cannot be exported.");
                            long hours = duration.Ticks / TimeSpan.TicksPerHour;
                            long seconds = duration.Ticks % TimeSpan.TicksPerHour / TimeSpan.TicksPerSecond;
                            sb.AppendLine("DURATION:PT" + hours.ToString(System.Globalization.CultureInfo.InvariantCulture) + "H" +
                                seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "S");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"DTSTART:{startUtc:yyyyMMdd'T'HHmmss'Z'}");
                        if (NominalDuration.HasValue(ev)) sb.AppendLine("DURATION:" + NominalDuration.Format(ev));
                        else sb.AppendLine($"DTEND:{endUtc:yyyyMMdd'T'HHmmss'Z'}");
                    }
                }
                else if (ev.IsSeries || NominalDuration.HasValue(ev))
                {
                    sb.AppendLine($"DTSTART:{ev.Start:yyyyMMdd'T'HHmmss}");
                    if (NominalDuration.HasValue(ev)) sb.AppendLine("DURATION:" + NominalDuration.Format(ev));
                    else sb.AppendLine($"DTEND:{ev.End:yyyyMMdd'T'HHmmss}");
                }
                else
                {
                    sb.AppendLine($"DTSTART:{ev.Start.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");
                    sb.AppendLine($"DTEND:{ev.End.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}");
                }

                if (ev.IsOverride && ev.OccurrenceAnchor.HasValue)
                    sb.AppendLine(FoldLine(IdentityProperty("RECURRENCE-ID", ev.OccurrenceAnchor.Value,
                        ev.OccurrenceBasis, ev.OccurrenceZoneId)));

                if (ev.IsSeries)
                {
                    sb.AppendLine(FoldLine(BuildRRule(ev)));

                    if (ev.AllDay)
                    {
                        foreach (var day in ev.SkipDates)
                            sb.AppendLine(IdentityProperty("EXDATE", day, OccurrenceTimeBasis.Date, ""));
                    }
                    else
                    {
                        var skipped = new HashSet<long>();
                        foreach (var nominal in ev.SkipOccurrences) skipped.Add(nominal.Ticks);
                        var pattern = ev.Clone();
                        pattern.SkipDates = [];
                        pattern.SkipOccurrences = [];
                        foreach (var day in ev.SkipDates)
                        {
                            var end = day.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : day.Date.AddDays(1);
                            foreach (var occurrence in Recurrence.Expand(pattern, day.Date, end))
                                if (occurrence.Start.Date == day.Date)
                                    skipped.Add(Recurrence.CanonicalOccurrenceStart(ev, occurrence).Ticks);
                        }
                        var basis = ev.RepeatTimeBasis == RecurrenceTimeBasis.Utc ? OccurrenceTimeBasis.Utc :
                            ev.RepeatTimeBasis == RecurrenceTimeBasis.SourceZone ? OccurrenceTimeBasis.SourceZone : OccurrenceTimeBasis.Floating;
                        foreach (long ticks in skipped)
                            sb.AppendLine(FoldLine(IdentityProperty("EXDATE", new DateTime(ticks), basis, ev.RepeatZoneId)));
                    }
                }

                sb.AppendLine(FoldLine($"SUMMARY:{EscapeText(ev.Title)}"));

                if (!string.IsNullOrEmpty(ev.Location))
                    sb.AppendLine(FoldLine($"LOCATION:{EscapeText(ev.Location)}"));

                if (!string.IsNullOrEmpty(ev.Description))
                    sb.AppendLine(FoldLine($"DESCRIPTION:{EscapeText(ev.Description)}"));

                foreach (var attendee in ev.Attendees)
                    sb.AppendLine(FoldLine($"ATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:{attendee}"));

                // CATEGORIES is a comma-separated LIST, so each name is escaped on its own and
                // the separators are then written raw. Running EscapeText over the whole string
                // would turn every separator into "\," and collapse the list into one category
                // named "Work\, Client" - the mistake this property invites.
                if (!string.IsNullOrEmpty(ev.Categories))
                {
                    var escaped = new List<string>();
                    foreach (string name in EventStore.SplitCategories(ev.Categories))
                        escaped.Add(EscapeText(name));
                    if (escaped.Count > 0)
                        sb.AppendLine(FoldLine("CATEGORIES:" + string.Join(",", escaped)));
                }

                sb.AppendLine("END:VEVENT");
            }

            sb.AppendLine("END:VCALENDAR");
            return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        /// <summary>
        /// The series' repeat rule. COUNT and UNTIL are mutually exclusive in RFC 5545, so only
        /// one is ever written - COUNT wins, matching how the editor treats them.
        /// </summary>
        private static string BuildRRule(CalendarEvent ev)
        {
            var sb = new StringBuilder("RRULE:FREQ=");
            switch (ev.Repeat)
            {
                case RepeatFreq.Daily:   sb.Append("DAILY");   break;
                case RepeatFreq.Weekly:  sb.Append("WEEKLY");  break;
                case RepeatFreq.Monthly: sb.Append("MONTHLY"); break;
                default:                 sb.Append("YEARLY");  break;
            }

            if (ev.RepeatInterval > 1) sb.Append(";INTERVAL=").Append(ev.RepeatInterval);

            if (ev.Repeat == RepeatFreq.Weekly && ev.RepeatDays != null && ev.RepeatDays.Count > 0)
            {
                var codes = new List<string>();
                foreach (var d in ev.RepeatDays) codes.Add(WeekdayCode(d));
                sb.Append(";BYDAY=").Append(string.Join(",", codes));
            }

            if (ev.Repeat == RepeatFreq.Weekly)
                sb.Append(";WKST=").Append(WeekdayCode(ev.RepeatWeekStart));

            if (ev.RepeatCount > 0)
                sb.Append(";COUNT=").Append(ev.RepeatCount);
            else if (ev.RepeatUntilExact.HasValue || ev.RepeatUntil.HasValue)
            {
                var until = ev.RepeatUntilExact ?? ev.RepeatUntil!.Value.Date.AddHours(23).AddMinutes(59).AddSeconds(59);
                if (ev.AllDay) sb.Append(";UNTIL=").Append(until.ToString("yyyyMMdd"));
                else if (ev.RepeatTimeBasis == RecurrenceTimeBasis.Local)
                    sb.Append(";UNTIL=").Append(until.ToString("yyyyMMdd'T'HHmmss"));
                else
                {
                    if (until.Kind != DateTimeKind.Utc) until = DateTime.SpecifyKind(until, DateTimeKind.Local).ToUniversalTime();
                    sb.Append(";UNTIL=").Append(until.ToString("yyyyMMdd'T'HHmmss'Z'"));
                }
            }

            return sb.ToString();
        }

        private static string IdentityProperty(string name, DateTime value, OccurrenceTimeBasis basis, string zone)
        {
            if (basis == OccurrenceTimeBasis.Date) return name + ";VALUE=DATE:" + value.ToString("yyyyMMdd");
            if (basis == OccurrenceTimeBasis.SourceZone) return name + ";TZID=\"" + zone + "\":" + value.ToString("yyyyMMdd'T'HHmmss");
            return name + ":" + value.ToString(basis == OccurrenceTimeBasis.Utc ? "yyyyMMdd'T'HHmmss'Z'" : "yyyyMMdd'T'HHmmss");
        }

        private static string EscapeText(string s)
            => s.Replace("\\", "\\\\").Replace(",", "\\,")
                .Replace(";", "\\;").Replace("\n", "\\n");

        /// <summary>RFC-5545 line folding at 75 octets.</summary>
        private static string FoldLine(string line)
        {
            var sb = new StringBuilder();
            int octets = 0;
            int index = 0;
            while (index < line.Length)
            {
                char c = line[index];
                int chars = 1;
                int bytes;
                if (char.IsHighSurrogate(c) && index + 1 < line.Length &&
                    char.IsLowSurrogate(line[index + 1]))
                {
                    chars = 2;
                    bytes = 4;
                }
                else bytes = c <= 0x7f ? 1 : c <= 0x7ff ? 2 : 3;

                if (octets + bytes > 75)
                {
                    sb.Append("\r\n ");
                    octets = 1;
                }
                sb.Append(line, index, chars);
                octets += bytes;
                index += chars;
            }
            return sb.ToString();
        }
    }
}
