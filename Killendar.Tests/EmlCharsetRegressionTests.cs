using System;
using System.IO;
using System.Text;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public class EmlCharsetRegressionTests
    {
        [Fact]
        public void Windows1252EightBitInvitePreservesOriginalBytes()
        {
            AssertInvite("windows-1252", "8bit");
        }

        [Theory]
        [InlineData("utf-8")]
        [InlineData("windows-1252")]
        [InlineData("iso-8859-1")]
        public void CharsetIsAppliedAfterEachTransferEncoding(string charset)
        {
            foreach (string transfer in new[] { "8bit", "binary", "base64", "quoted-printable" })
                AssertInvite(charset, transfer);
        }

        [Fact]
        public void NestedMultipartPreservesEightBitCharset()
        {
            AssertInvite("windows-1252", "8bit", nested: true);
        }

        [Fact]
        public void NonMimeFallbackPreservesUtf8Text()
        {
            AssertInvite("utf-8", "8bit", raw: true);
        }

        private static void AssertInvite(string charset, string transfer, bool nested = false, bool raw = false)
        {
            const string calendar = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Caf\u00e9\nLOCATION:Montr\u00e9al\nDESCRIPTION:R\u00e9union\nEND:VEVENT\nEND:VCALENDAR\n";
            byte[] payload = Encoding.GetEncoding(charset).GetBytes(calendar.Replace("\n", "\r\n"));
            if (transfer == "base64")
                payload = Encoding.ASCII.GetBytes(Convert.ToBase64String(payload));
            else if (transfer == "quoted-printable")
            {
                var encoded = new StringBuilder();
                foreach (byte value in payload)
                    encoded.Append('=').Append(value.ToString("X2"));
                payload = Encoding.ASCII.GetBytes(encoded.ToString());
            }
            string folder = Path.Combine(TestScratch.Root + @"\eml", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "invite.eml");
            try
            {
                byte[] headers = Encoding.ASCII.GetBytes("MIME-Version: 1.0\r\nContent-Type: text/calendar; charset=" + charset + "\r\nContent-Transfer-Encoding: " + transfer + "\r\n\r\n");
                byte[] suffix = new byte[0];
                if (raw)
                {
                    headers = Encoding.ASCII.GetBytes("Saved invite\r\n");
                    suffix = Encoding.ASCII.GetBytes("End of saved invite\r\n");
                }
                else if (nested)
                {
                    headers = Encoding.ASCII.GetBytes("MIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=outer\r\n\r\nPreamble\r\n--outer\r\nContent-Type: multipart/alternative; boundary=inner\r\n\r\n--inner\r\nContent-Type: text/plain\r\n\r\nSee attached invite.\r\n--inner\r\n" + Encoding.ASCII.GetString(headers));
                    suffix = Encoding.ASCII.GetBytes("--inner--\r\n--outer--\r\nEpilogue\r\n");
                }
                using (var stream = File.Create(path))
                {
                    stream.Write(headers, 0, headers.Length);
                    stream.Write(payload, 0, payload.Length);
                    stream.Write(suffix, 0, suffix.Length);
                }
                Assert.Equal(raw ? calendar.TrimEnd('\n') : calendar,
                    EmlService.ExtractCalendarText(path)?.Replace("\r\n", "\n"));
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(folder);
            }
        }
    }
}
