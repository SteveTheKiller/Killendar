using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Killendar.Services
{
    /// <summary>
    /// Pulls the calendar invite out of a saved email. An emailed invite is a MIME message with a
    /// text/calendar part somewhere inside it - usually base64 or quoted-printable encoded, often
    /// nested two multiparts deep (mixed containing alternative). This walks the structure, finds
    /// that part, decodes it and hands back the iCalendar text for IcsService to parse.
    ///
    /// Only .eml, deliberately. Outlook's .msg is a compound OLE binary and parsing it is a
    /// project of its own - out of scope, and saying so beats half-supporting it.
    /// </summary>
    public static class EmlService
    {
        /// <summary>The decoded iCalendar text, or null when the email carries no invite.</summary>
        public static string? ExtractCalendarText(string path)
        {
            // Read as UTF-8: the MIME structure and any base64/quoted-printable body are ASCII,
            // and an 8bit body in a saved invite is overwhelmingly UTF-8 already. A part that
            // declares another charset is re-decoded from bytes in DecodeBody.
            string raw = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");

            string? hit = FromEntity(raw);
            if (hit != null) return hit;

            // Last resort for non-MIME mails and mangled structure: an unencoded invite is
            // sitting in the text as-is. Cut the VCALENDAR block out directly.
            int begin = raw.IndexOf("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase);
            if (begin < 0) return null;
            int end = raw.LastIndexOf("END:VCALENDAR", StringComparison.OrdinalIgnoreCase);
            if (end < begin) return null;
            return raw.Substring(begin, end - begin) + "END:VCALENDAR";
        }

        /// <summary>One MIME entity: headers, blank line, body. Recurses through multiparts.</summary>
        private static string? FromEntity(string entity)
        {
            int split   = entity.IndexOf("\n\n", StringComparison.Ordinal);
            string head = split < 0 ? entity : entity.Substring(0, split);
            string body = split < 0 ? ""     : entity.Substring(split + 2);

            string ctype = HeaderValue(head, "Content-Type");
            string cte   = HeaderValue(head, "Content-Transfer-Encoding").Trim().ToLowerInvariant();

            if (ctype.StartsWith("text/calendar", StringComparison.OrdinalIgnoreCase))
                return DecodeBody(body, cte, Param(ctype, "charset"));

            if (ctype.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            {
                string boundary = Param(ctype, "boundary");
                if (boundary.Length == 0) return null;

                foreach (var part in SplitParts(body, boundary))
                {
                    var hit = FromEntity(part);
                    if (hit != null) return hit;
                }
            }

            return null;
        }

        /// <summary>
        /// A header's unfolded value, or "". Folded continuation lines (leading space or tab)
        /// belong to the header above them - Content-Type with a boundary is nearly always
        /// folded, so skipping this step loses the boundary and with it the whole walk.
        /// </summary>
        private static string HeaderValue(string head, string name)
        {
            var lines = head.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                if (!line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var sb = new StringBuilder(line.Substring(colon + 1).Trim());
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].Length == 0 || (lines[j][0] != ' ' && lines[j][0] != '\t')) break;
                    sb.Append(' ').Append(lines[j].Trim());
                }
                return sb.ToString();
            }
            return "";
        }

        /// <summary>A parameter ("boundary", "charset") from a header value, quotes stripped.</summary>
        private static string Param(string headerValue, string name)
        {
            var m = Regex.Match(headerValue,
                name + @"\s*=\s*(?:""(?<q>[^""]*)""|(?<t>[^;\s]+))",
                RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            return m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["t"].Value;
        }

        /// <summary>
        /// The parts between "--boundary" delimiter lines. The closing "--boundary--" ends the
        /// walk; a preamble before the first delimiter and an epilogue after the last are
        /// discarded, per RFC 2046.
        /// </summary>
        private static List<string> SplitParts(string body, string boundary)
        {
            var parts     = new List<string>();
            var lines     = body.Split('\n');
            var current   = new StringBuilder();
            bool inPart   = false;
            string open   = "--" + boundary;
            string close  = "--" + boundary + "--";

            foreach (var line in lines)
            {
                string trimmed = line.TrimEnd();
                if (trimmed == open || trimmed == close)
                {
                    if (inPart && current.Length > 0) parts.Add(current.ToString());
                    current.Clear();
                    if (trimmed == close) break;
                    inPart = true;
                    continue;
                }
                if (inPart) current.Append(line).Append('\n');
            }

            return parts;
        }

        private static string? DecodeBody(string body, string cte, string charset)
        {
            try
            {
                switch (cte)
                {
                    case "base64":
                    {
                        var bytes = Convert.FromBase64String(
                            Regex.Replace(body, @"\s+", ""));
                        return CharsetFor(charset).GetString(bytes);
                    }

                    case "quoted-printable":
                        return DecodeQuotedPrintable(body, CharsetFor(charset));

                    // 7bit, 8bit, binary, or nothing declared: the text is the text.
                    default:
                        return body;
                }
            }
            catch (FormatException)
            {
                // A corrupt base64 body. Nothing to salvage from this part; the caller keeps
                // walking and the raw-text fallback still gets its chance.
                return null;
            }
        }

        private static Encoding CharsetFor(string charset)
        {
            if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
            try { return Encoding.GetEncoding(charset); }
            catch (ArgumentException) { return Encoding.UTF8; }
        }

        /// <summary>
        /// Quoted-printable: "=XX" is a byte, "=" at end of line is a soft break. Decoded to
        /// BYTES first and then through the declared charset - decoding to chars directly would
        /// break every multi-byte UTF-8 sequence.
        /// </summary>
        private static string DecodeQuotedPrintable(string body, Encoding enc)
        {
            var bytes = new List<byte>(body.Length);
            int i = 0;
            while (i < body.Length)
            {
                char c = body[i];
                if (c == '=')
                {
                    // Soft line break: "=" followed by the line end.
                    if (i + 1 < body.Length && body[i + 1] == '\n') { i += 2; continue; }

                    if (i + 2 < body.Length &&
                        int.TryParse(body.Substring(i + 1, 2),
                                     System.Globalization.NumberStyles.HexNumber,
                                     System.Globalization.CultureInfo.InvariantCulture, out int b))
                    {
                        bytes.Add((byte)b);
                        i += 3;
                        continue;
                    }
                }

                // Plain text is ASCII by the RFC; anything stray is kept as its low byte rather
                // than thrown away.
                bytes.Add(unchecked((byte)c));
                i++;
            }
            return enc.GetString([.. bytes]);
        }
    }
}
