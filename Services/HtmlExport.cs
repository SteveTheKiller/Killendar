using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// Writes the calendar as a styled, self-contained web page: one month grid per month in the
    /// range. This is the export that covers "I want to print my calendar" - the page carries a
    /// print stylesheet that flips to ink-friendly white, so Ctrl+P and Windows' print-to-PDF
    /// finish the job without Killendar owning a PDF layout engine.
    ///
    /// The page carries its own theme and accent switchers, KillerScan's report pattern
    /// (Export.cs there is the reference): all six family palettes are embedded as CSS variable
    /// blocks, the page opens in whatever theme the app was in when it exported, and the reader's
    /// choice persists in localStorage. No network, no dependencies - the file stays a single
    /// self-contained page.
    ///
    /// The caller hands in ALREADY-EXPANDED events (EventStore.GetInRange), so repeats, series
    /// overrides and skipped dates all land on the right days without this file knowing repeats
    /// exist - same contract as the views.
    /// </summary>
    public static class HtmlExport
    {
        public static void ExportToFile(List<CalendarEvent> events, DateTime from, DateTime toExclusive, string path)
        {
            File.WriteAllText(path, Build(events, from, toExclusive),
                              new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static string Build(List<CalendarEvent> events, DateTime from, DateTime toExclusive)
        {
            var culture = CultureInfo.CurrentCulture;
            from        = from.Date;
            toExclusive = toExclusive.Date;
            if (toExclusive <= from) toExclusive = from.AddDays(1);

            // Month/weekday names and week start all come from Windows' culture, the same way the
            // in-app views get them - so the page reads like the app on the machine it came from.
            DayOfWeek weekStart = WeekStartManager.FirstDay;

            var sb = new StringBuilder();
            string rangeLabel = DateFormatManager.Format(from) + " - "
                              + DateFormatManager.Format(toExclusive.AddDays(-1));

            // Opens in the theme the app is wearing right now, so the page looks like the app
            // that made it; the switcher can take it anywhere from there.
            string current = ThemeManager.Current.ToString().ToLowerInvariant();

            sb.Append("<!DOCTYPE html>\n<html lang=\"")
              .Append(Html(culture.TwoLetterISOLanguageName))
              .Append("\" class=\"theme-").Append(current)
              .Append("\">\n<head>\n<meta charset=\"utf-8\">\n<title>Killendar ")
              .Append(Html(rangeLabel))
              .Append("</title>\n<style>\n").Append(ThemeCss()).Append(Css)
              .Append("</style>\n</head>\n<body>\n");

            sb.Append("<header><div class=\"wm\">The <span>Killendar</span></div>")
              .Append("<div class=\"side\">")
              .Append("<div class=\"swrow\"><span class=\"swlabel\">Theme</span><div class=\"sw\" id=\"themesw\"></div></div>")
              .Append("<div class=\"swrow\"><span class=\"swlabel\">Accent</span><div class=\"sw\" id=\"accentsw\"></div></div>")
              .Append("<div class=\"range\">").Append(Html(rangeLabel)).Append("</div>")
              .Append("</div></header>\n<main>\n");

            var monthStart = new DateTime(from.Year, from.Month, 1);
            while (monthStart < toExclusive)
            {
                AppendMonth(sb, events, monthStart, from, toExclusive, culture, weekStart);
                monthStart = monthStart.AddMonths(1);
            }

            sb.Append("</main>\n<footer>")
              .Append(Html(DateFormatManager.Format(DateTime.Today)))
              .Append(" - killendar.net</footer>\n<script>\n")
              .Append(SwitcherScript(current))
              .Append("</script>\n</body>\n</html>\n");

            return sb.ToString();
        }

        private static void AppendMonth(StringBuilder sb, List<CalendarEvent> events,
                                        DateTime monthStart, DateTime from, DateTime toExclusive,
                                        CultureInfo culture, DayOfWeek weekStart)
        {
            sb.Append("<section class=\"month\">\n<h2>")
              .Append(Html(monthStart.ToString("MMMM yyyy", culture)))
              .Append("</h2>\n<table>\n<thead><tr>");

            for (int i = 0; i < 7; i++)
            {
                var d = (DayOfWeek)(((int)weekStart + i) % 7);
                sb.Append("<th>").Append(Html(culture.DateTimeFormat.AbbreviatedDayNames[(int)d])).Append("</th>");
            }
            sb.Append("</tr></thead>\n<tbody>\n");

            // Back up to the week's first day, exactly as MonthView lays its grid.
            var cell = monthStart;
            while (cell.DayOfWeek != weekStart) cell = cell.AddDays(-1);

            var monthEnd = monthStart.AddMonths(1);
            while (cell < monthEnd)
            {
                sb.Append("<tr>");
                for (int i = 0; i < 7; i++, cell = cell.AddDays(1))
                {
                    if (cell.Month != monthStart.Month)
                    {
                        // Out-of-month cells stay empty rather than showing the neighbor month's
                        // appointments again - on paper a duplicate reads as a second appointment.
                        sb.Append("<td class=\"out\"></td>");
                        continue;
                    }

                    bool inRange = cell >= from && cell < toExclusive;
                    sb.Append(inRange ? "<td>" : "<td class=\"off\">");
                    sb.Append("<div class=\"dn\">").Append(cell.Day).Append("</div>");

                    if (inRange)
                    {
                        foreach (var ev in events)
                        {
                            if (!ev.OccursOn(cell)) continue;

                            if (ev.AllDay)
                            {
                                sb.Append("<div class=\"ev allday\">")
                                  .Append(Html(TitleOf(ev))).Append("</div>");
                            }
                            else if (ev.Start.Date == cell.Date)
                            {
                                sb.Append("<div class=\"ev\"><span class=\"tm\">")
                                  .Append(Html(ev.Start.ToString("t", culture)))
                                  .Append("</span> ").Append(Html(TitleOf(ev))).Append("</div>");
                            }
                            else
                            {
                                // A later day of a multi-day appointment: title only, dimmed
                                // time slot, so the span is visible without restating the clock.
                                sb.Append("<div class=\"ev cont\">")
                                  .Append(Html(TitleOf(ev))).Append("</div>");
                            }
                        }
                    }

                    sb.Append("</td>");
                }
                sb.Append("</tr>\n");
            }

            sb.Append("</tbody>\n</table>\n</section>\n");
        }

        private static string TitleOf(CalendarEvent ev)
            => string.IsNullOrWhiteSpace(ev.Title) ? LocaleManager.Loc("Str_Cal_NoTitle") : ev.Title;

        private static string Html(string s) => WebUtility.HtmlEncode(s ?? "");

        // ── The six family palettes, embedded as CSS variable blocks ─────────────
        // Neutral hexes from KillerScan's report table (the family's shared values); the three
        // neutral families carry Killendar's OWN reds - Dark #DD504B, Light #931A1A, Black
        // #FF2929 - because each neutral family defines its own red and Killendar's branding
        // keys off Dark's (ThemeManager.cs has the full story).
        private readonly struct ReportTheme(string key, string bg, string surface, string accent,
                                            string text, string muted, string dim, string border, string outCell)
        {
            public readonly string Key = key, Bg = bg, Surface = surface, Accent = accent,
                                   Text = text, Muted = muted, Dim = dim, Border = border, Out = outCell;
        }

        private static readonly ReportTheme[] Themes =
        [
            new("dark",     "#1c1c1c", "#333333", "#DD504B", "#e0e0e0", "#a0a0a0", "#6a6a6a", "#2e2e2e", "#191919"),
            new("light",    "#dcdcdc", "#f0f0f0", "#931A1A", "#1a1a1a", "#555555", "#8a8a8a", "#b0b0b0", "#d2d2d2"),
            new("black",    "#000000", "#0d0d0d", "#FF2929", "#ffffff", "#cccccc", "#8a8a8a", "#2a2a2a", "#0a0a0a"),
            new("blood",    "#240c0d", "#2c1012", "#e8485a", "#fffde8", "#f8c99e", "#c99f83", "#401d1d", "#1d090a"),
            new("greed",    "#002115", "#002e1c", "#3fbf6f", "#fffde8", "#e0d49a", "#a89f74", "#0f4a30", "#001a10"),
            new("cyanotic", "#001a28", "#00263a", "#3aa0d8", "#fffde8", "#e0d49a", "#a89f74", "#183450", "#001420"),
        ];

        /// <summary>One html.theme-X variable block per palette, built without interpolation so
        /// the braces stay readable (KillerScan's approach, for KillerScan's reason).</summary>
        private static string ThemeCss()
        {
            var b = new StringBuilder();
            foreach (var t in Themes)
                b.Append("html.theme-").Append(t.Key).Append('{')
                 .Append("--bg:").Append(t.Bg).Append(';')
                 .Append("--surface:").Append(t.Surface).Append(';')
                 .Append("--accent:").Append(t.Accent).Append(';')
                 .Append("--text:").Append(t.Text).Append(';')
                 .Append("--muted:").Append(t.Muted).Append(';')
                 .Append("--dim:").Append(t.Dim).Append(';')
                 .Append("--border:").Append(t.Border).Append(';')
                 .Append("--out:").Append(t.Out).Append(";}\n");
            return b.ToString();
        }

        /// <summary>
        /// The switcher rows: theme circles and accent circles, KillerScan's script adapted
        /// (kdTheme / kdAccent keys). The reader's picks persist in localStorage and win over
        /// the exported default on the next open.
        /// </summary>
        private static string SwitcherScript(string current)
        {
            var b = new StringBuilder();
            b.AppendLine("var THEMES=[['dark','Dark','#3a3a3a'],['light','Light','#e8e8e8'],['black','Black','#000000'],['blood','Blood','#4a1f20'],['greed','Greed','#0a5234'],['cyanotic','Cyanotic','#0a4a6e']];");
            b.AppendLine("var sw=document.getElementById('themesw');");
            b.AppendLine("function setTheme(t){document.documentElement.className='theme-'+t;try{localStorage.setItem('kdTheme',t)}catch(e){}var k=sw.children;for(var i=0;i<k.length;i++)k[i].className=(k[i].getAttribute('data-t')===t)?'active':'';}");
            b.AppendLine("THEMES.forEach(function(a){var x=document.createElement('button');x.title=a[1];x.setAttribute('data-t',a[0]);x.style.background=a[2];x.onclick=function(){setTheme(a[0])};sw.appendChild(x);});");
            b.AppendLine("var saved=null;try{saved=localStorage.getItem('kdTheme')}catch(e){}");
            b.Append("setTheme(saved||'").Append(current).AppendLine("');");
            b.AppendLine("var ACCENTS=[['#DD504B','Red'],['#E8962C','Orange'],['#1ea54c','Green'],['#1FB8A8','Teal'],['#50AEE8','Blue'],['#B982E3','Purple']];");
            b.AppendLine("var asw=document.getElementById('accentsw');");
            b.AppendLine("function setAccent(c){if(c){document.documentElement.style.setProperty('--accent',c);try{localStorage.setItem('kdAccent',c)}catch(e){}}var k=asw.children;for(var i=0;i<k.length;i++)k[i].className=(k[i].getAttribute('data-c')===c)?'active':'';}");
            b.AppendLine("ACCENTS.forEach(function(a){var x=document.createElement('button');x.title=a[1];x.setAttribute('data-c',a[0]);x.style.background=a[0];x.onclick=function(){setAccent(a[0])};asw.appendChild(x);});");
            b.AppendLine("var savedA=null;try{savedA=localStorage.getItem('kdAccent')}catch(e){}");
            b.AppendLine("if(savedA)setAccent(savedA);");
            return b.ToString();
        }

        // Everything colored reads from the variables, so the switcher repaints the whole page.
        // Print stays FIXED light regardless of the on-screen theme - a fridge copy should not
        // cost a cartridge because the reader happened to be on Black.
        private const string Css = @"
* { box-sizing: border-box; margin: 0; padding: 0; }
body { background: var(--bg); color: var(--text); font-family: 'Segoe UI', sans-serif; padding: 28px; }
header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 22px; }
.wm { font-size: 24px; }
.wm span { color: var(--accent); font-weight: bold; font-size: 30px; }
.side { display: flex; flex-direction: column; gap: 6px; align-items: flex-end; }
.swrow { display: flex; align-items: center; gap: 8px; }
.swlabel { color: var(--muted); font-size: 10px; font-family: Consolas, monospace;
           letter-spacing: .5px; text-transform: uppercase; }
.sw { display: flex; gap: 7px; align-items: center; }
.sw button { width: 18px; height: 18px; border-radius: 50%; border: 2px solid var(--border);
             cursor: pointer; padding: 0; outline: none; transition: transform .1s; }
.sw button:hover { transform: scale(1.15); }
.sw button.active { border-color: var(--text); }
.range { color: var(--muted); font-family: Consolas, monospace; font-size: 13px; }
.month { margin-bottom: 30px; }
.month h2 { font-size: 18px; font-weight: 600; margin-bottom: 8px; }
table { width: 100%; border-collapse: collapse; table-layout: fixed; }
th { background: var(--surface); color: var(--muted); font-family: Consolas, monospace; font-size: 11px;
     font-weight: normal; text-transform: uppercase; padding: 5px 6px; text-align: left;
     border: 1px solid var(--border); }
td { border: 1px solid var(--border); vertical-align: top; padding: 4px 5px 6px;
     height: 84px; overflow: hidden; }
td.out { background: var(--out); }
td.off .dn { color: var(--border); }
.dn { font-family: Consolas, monospace; font-size: 12px; color: var(--dim); margin-bottom: 3px; }
.ev { font-size: 11.5px; line-height: 1.35; margin-bottom: 2px; border-left: 2px solid var(--accent);
      padding-left: 5px; word-wrap: break-word; }
.ev.allday { background: var(--accent); color: #fff; border-left: none; border-radius: 2px;
             padding: 1px 5px; }
.ev.cont { border-left-color: var(--dim); color: var(--muted); }
.tm { font-family: Consolas, monospace; color: var(--muted); font-size: 10.5px; }
footer { color: var(--dim); font-family: Consolas, monospace; font-size: 11px; margin-top: 10px; }
@media print {
  body { background: #fff; color: #111; }
  .sw, .swrow { display: none; }
  th { background: #eee; color: #555; border-color: #bbb; }
  td { border-color: #bbb; }
  td.out { background: #f6f6f6; }
  .dn { color: #888; }
  .ev.cont { color: #666; border-left-color: #999; }
  .tm { color: #555; }
  .range, footer { color: #777; }
  .month { page-break-inside: avoid; }
}
";
    }
}
