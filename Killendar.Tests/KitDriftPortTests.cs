using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GC-009 and GL-005: Killendar took the fixes KillerUI already carries.</summary>
    public sealed class KitDriftPortTests
    {
        [Fact]
        public void DialogsReferenceThemeBrushesDynamically()
        {
            // A theme switch replaces the brush dictionary, so a StaticResource brush freezes the
            // dialog in the theme it opened under.
            var offenders = Directory.GetFiles(Path.Combine(KitTestHost.Root(), "Controls"), "*.xaml")
                .Where(f => !f.EndsWith("Controls.xaml", StringComparison.Ordinal)
                         && !f.EndsWith("AppStyles.xaml", StringComparison.Ordinal))
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{StaticResource\s+(\w*Brush)\}")
                    .Cast<Match>().Select(m => Path.GetFileName(f) + ": " + m.Groups[1].Value))
                .ToList();
            Assert.True(offenders.Count == 0, string.Join("; ", offenders));
        }

        [Fact]
        public void ThePickerModifiedLabelUsesTheInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
                var entryType = KitTestHost.AppType("Killendar.Controls.PickerEntry");
                var entry = Activator.CreateInstance(entryType, "a.txt", @"C:\a.txt", false, 1L, new DateTime(2026, 11, 14, 14, 30, 0))!;
                Assert.Equal("2026-11-14 14:30", (string)entryType.GetProperty("ModifiedLabel")!.GetValue(entry)!);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
