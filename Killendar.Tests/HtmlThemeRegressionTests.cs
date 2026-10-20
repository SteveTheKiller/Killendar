using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class HtmlThemeRegressionTests
    {
        [Fact]
        public void BuildIncludesAllThirteenThemesAndTheClassicThemeSelection()
        {
            string html = BuildCalendar();

            Assert.Contains("html.theme-se98{", html);
            Assert.Contains("['se98','98SE'", html);
            Assert.Equal(13, Regex.Matches(html, @"html\.theme-[a-z0-9]+\{").Count);
            foreach (Theme theme in Enum.GetValues(typeof(Theme)))
                Assert.Contains("['" + theme.ToString().ToLowerInvariant() + "','", html);
        }

        [Fact]
        public void BuildExposesAllEightAccentChoicesAndAccessibleSelectionButtons()
        {
            string html = BuildCalendar();

            Assert.Contains("['#EAD900','Yellow']", html);
            Assert.Contains("['#FF6CD1','Magenta']", html);
            Assert.Contains("aria-pressed", html);
            Assert.Contains("aria-label", html);
            Assert.Contains(".sw button:focus-visible", html);
        }

        [Theory]
        [InlineData("se98", "#c0c0c0", "#004f00", "#ffffff")]
        [InlineData("ectoplasm", "linear-gradient(90deg,#5e1764 0%,#25232b 100%)", "#ead900", "#293c3f")]
        [InlineData("decay", "linear-gradient(90deg,#3b3f46 0%,#50564b 100%)", "#d1c8aa", "#403f3b")]
        [InlineData("mourning", "#3b3642", "#ffa9bd", "#554c5d")]
        [InlineData("sepulchre", "linear-gradient(90deg,#25282b 0%,#2c5458 100%)", "#8ccac8", "#3a352f")]
        [InlineData("delirium", "linear-gradient(90deg,#641d25 0%,#2f2853 100%)", "#f29200", "#343344")]
        [InlineData("malaise", "linear-gradient(90deg,#563343 0%,#365b58 100%)", "#ffb4c6", "#232a2b")]
        public void AddedPalettesUseTheApprovedThemeBrushValues(string theme, string background, string primary, string pane)
        {
            string html = BuildCalendar();
            var block = Regex.Match(html, @"html\.theme-" + theme + @"\{([^}]+)\}").Groups[1].Value;

            Assert.Contains("--bg:" + background + ";", block);
            Assert.Contains("--accent:" + primary + ";", block);
            Assert.Contains("--out:" + pane + ";", block);
        }

        [Fact]
        public void ExistingDarkPaletteKeepsItsEstablishedExportColors()
        {
            Assert.Contains("html.theme-dark{--bg:#1c1c1c;--surface:#333333;--accent:#DD504B;--text:#e0e0e0;--muted:#a0a0a0;--dim:#6a6a6a;--border:#2e2e2e;--out:#191919;}", BuildCalendar());
        }

        private static string BuildCalendar()
        {
            // Register WPF's pack URI scheme without creating an Application or theme resources.
            System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
            return HtmlExport.Build(new List<CalendarEvent>(), new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));
        }
    }
}
