using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // KD-J (IE-009): Outlook's CSV export is in the ANSI code page, not UTF-8.
    [Collection("Calendar settings")]
    public sealed class CsvEncodingRegressionTests
    {
        private const string Csv = "Subject,Start Date,Start Time,End Date,End Time\r\nRéunion à Montréal,2026-11-03,09:00,2026-11-03,10:00\r\n";

        // "Réunion à Montréal" as Windows-1252 writes it: é is 0xE9, à is 0xE0.
        private static readonly byte[] Windows1252 = Encoding.ASCII.GetBytes(Csv.Replace("é", "\u0001").Replace("à", "\u0002"))
            .Select(b => b == 1 ? (byte)0xE9 : b == 2 ? (byte)0xE0 : b).ToArray();

        private const string Expected = "Réunion à Montréal";

        [Fact]
        public void AnsiBytesDecodeThroughTheAnsiCodePage()
        {
            string text = CsvService.DecodeText(Windows1252, Encoding.GetEncoding(1252));

            Assert.Equal(Expected, Assert.Single(CsvService.ParseText(text).Events).Title);
        }

        [Fact]
        public void UTF8IsNeverReadAsAnsi()
        {
            string text = CsvService.DecodeText(new UTF8Encoding(false).GetBytes(Csv), Encoding.GetEncoding(1252));

            Assert.Equal(Expected, Assert.Single(CsvService.ParseText(text).Events).Title);
        }

        private static string ImportFile(byte[] bytes)
        {
            string path = Path.Combine(Path.GetTempPath(), "KillendarCsvEncoding-" + Guid.NewGuid().ToString("N") + ".csv");
            var oldCurrent = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                File.WriteAllBytes(path, bytes);
                return Assert.Single(CsvService.ParseFile(path).Events).Title;
            }
            finally
            {
                CultureInfo.CurrentCulture = oldCurrent;
                File.Delete(path);
            }
        }

        [Fact]
        public void AnAnsiFileImportsWithoutReplacementCharacters()
        {
            string title = ImportFile(Windows1252);

            Assert.DoesNotContain('�', title);
            Assert.Equal(Expected.Length, title.Length);
        }

        [Fact]
        public void UTF8FilesWithAndWithoutAMarkStillImport()
        {
            Assert.Equal(Expected, ImportFile(new UTF8Encoding(false).GetBytes(Csv)));
            Assert.Equal(Expected, ImportFile(new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(Csv)).ToArray()));
        }

        [Fact]
        public void AUnicodeFileWithAMarkStillImports()
        {
            var unicode = new UnicodeEncoding(false, true);
            Assert.Equal(Expected, ImportFile(unicode.GetPreamble().Concat(unicode.GetBytes(Csv)).ToArray()));
        }
    }
}
