using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Killendar.Models;

namespace Killendar.Services
{
    public enum MeetingReply { NeedsAction, Accepted, Declined, Tentative, Delegated }

    public sealed record MeetingAttendee(string Address, string Name, MeetingReply Reply);

    /// <summary>
    /// The meeting details an imported invitation carries in its kept lines (CalendarEvent.
    /// KeptProperties): who organized it, who answered what, whether it shows as free, whether it
    /// is private, and a link to join. Read-only: the organizer owns these. Show-as-free is the
    /// one Killendar writes, as the standard TRANSP line. A link is offered only when it is an
    /// ordinary web address, and only opens when the user clicks it.
    /// </summary>
    public sealed class MeetingInfo
    {
        public string OrganizerName { get; private set; } = "";
        public string OrganizerAddress { get; private set; } = "";
        public List<MeetingAttendee> Attendees { get; } = [];
        public bool Free { get; private set; }
        public bool Private { get; private set; }
        public string? Link { get; private set; }

        private static readonly string[] LinkProperties =
            ["URL", "X-MICROSOFT-SKYPETEAMSMEETINGURL", "X-MICROSOFT-ONLINEMEETINGCONFLINK", "X-GOOGLE-CONFERENCE"];

        private static readonly Regex MeetingHost = new(
            @"https?://[^\s<>""]*(?:teams\.microsoft\.com|teams\.live\.com|zoom\.us|meet\.google\.com|webex\.com|whereby\.com|gotomeeting\.com)[^\s<>""]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The invite's revision number (SEQUENCE), 0 when it carries none. An organizer
        /// raises it each time the meeting changes, so a higher one is the newer invite.</summary>
        public static int Sequence(CalendarEvent ev)
        {
            foreach (var line in ev.KeptProperties)
            {
                var (name, _, value) = Split(line);
                if (name == "SEQUENCE" && int.TryParse(value.Trim(), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int sequence)) return sequence;
            }
            return 0;
        }

        /// <summary>The meeting in <paramref name="ev"/>, or null when it has no organizer or
        /// attendee lines - a plain appointment.</summary>
        public static MeetingInfo? From(CalendarEvent ev)
        {
            var info = new MeetingInfo { Free = IsFree(ev) };
            bool meeting = false;
            bool inAlarm = false;
            foreach (var line in ev.KeptProperties)
            {
                var (name, parameters, value) = Split(line);
                // A reminder's lines are kept with the meeting's; an EMAIL alarm's ATTENDEE is
                // the address it writes to, not someone invited.
                if (name == "BEGIN" && value.Trim().Equals("VALARM", StringComparison.OrdinalIgnoreCase)) { inAlarm = true; continue; }
                if (name == "END" && value.Trim().Equals("VALARM", StringComparison.OrdinalIgnoreCase)) { inAlarm = false; continue; }
                if (inAlarm) continue;
                switch (name)
                {
                    case "ORGANIZER":
                        meeting = true;
                        info.OrganizerAddress = Address(value);
                        info.OrganizerName = Param(parameters, "CN");
                        break;
                    case "ATTENDEE":
                        meeting = true;
                        info.Attendees.Add(new MeetingAttendee(Address(value), Param(parameters, "CN"),
                            Param(parameters, "PARTSTAT").ToUpperInvariant() switch
                            {
                                "ACCEPTED" => MeetingReply.Accepted,
                                "DECLINED" => MeetingReply.Declined,
                                "TENTATIVE" => MeetingReply.Tentative,
                                "DELEGATED" => MeetingReply.Delegated,
                                _ => MeetingReply.NeedsAction,
                            }));
                        break;
                    case "CLASS":
                        info.Private = value.Trim().Equals("PRIVATE", StringComparison.OrdinalIgnoreCase) ||
                                       value.Trim().Equals("CONFIDENTIAL", StringComparison.OrdinalIgnoreCase);
                        break;
                    default:
                        if (info.Link == null && Array.IndexOf(LinkProperties, name) >= 0 && SafeLink(value.Trim()))
                            info.Link = value.Trim();
                        break;
                }
            }
            if (!meeting) return null;
            if (info.Link == null)
                foreach (var text in new[] { ev.Location, ev.Description })
                    if (!string.IsNullOrEmpty(text) && MeetingHost.Match(text) is { Success: true } m && SafeLink(m.Value))
                    {
                        info.Link = m.Value.TrimEnd('.', ',', ')', ';');
                        break;
                    }
            return info;
        }

        /// <summary>Only an absolute http or https address with a host: nothing that runs a
        /// program, opens a file or a share, or executes script.</summary>
        public static bool SafeLink(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            !string.IsNullOrEmpty(uri.Host) && !uri.IsUnc;

        public static bool IsFree(CalendarEvent ev) =>
            ev.KeptProperties.Any(line =>
            {
                var (name, _, value) = Split(line);
                return name == "TRANSP" && value.Trim().Equals("TRANSPARENT", StringComparison.OrdinalIgnoreCase);
            });

        /// <summary>Shows the appointment as free (TRANSP:TRANSPARENT) or busy (no line, the
        /// standard default).</summary>
        public static void SetFree(CalendarEvent ev, bool free)
        {
            var kept = ev.KeptProperties.Where(line => Split(line).Name != "TRANSP").ToList();
            if (free) kept.Add("TRANSP:TRANSPARENT");
            ev.KeptProperties = kept;
        }

        private static string Address(string value)
        {
            var v = value.Trim();
            return v.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? v.Substring(7) : v;
        }

        private static string Param(string parameters, string key)
        {
            foreach (var part in SplitOutsideQuotes(parameters, ';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return part.Substring(eq + 1).Trim().Trim('"');
            }
            return "";
        }

        /// <summary>NAME;PARAMS:VALUE, with quoted parameter values that may hold ':' or ';'.</summary>
        private static (string Name, string Parameters, string Value) Split(string line)
        {
            bool quoted = false;
            int colon = -1, semi = -1;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') quoted = !quoted;
                else if (!quoted && c == ';' && semi < 0 && colon < 0) semi = i;
                else if (!quoted && c == ':') { colon = i; break; }
            }
            if (colon < 0) return (line.Trim().ToUpperInvariant(), "", "");
            int nameEnd = semi >= 0 ? semi : colon;
            string parameters = semi >= 0 ? line.Substring(semi + 1, colon - semi - 1) : "";
            return (line.Substring(0, nameEnd).Trim().ToUpperInvariant(), parameters, line.Substring(colon + 1));
        }

        private static IEnumerable<string> SplitOutsideQuotes(string text, char separator)
        {
            bool quoted = false;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"') quoted = !quoted;
                else if (!quoted && text[i] == separator)
                {
                    yield return text.Substring(start, i - start);
                    start = i + 1;
                }
            }
            if (start <= text.Length) yield return text.Substring(start);
        }
    }
}
