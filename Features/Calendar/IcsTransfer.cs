using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Killendar.Controls;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Features
{
    /// <summary>
    /// Import and export in every format Killendar speaks - .ics both ways, CSV both ways, .eml
    /// invites in, HTML out - plus the export scope flyout (whole calendar or a date range) and
    /// the warning that an export leaves an encrypted Killendar's contents in a plain text file.
    /// Which format runs is decided by the FILE's extension, both directions: the picker's filter
    /// combo already communicates the choices, so there is no separate format UI to keep in step.
    /// (Named for the days it spoke only iCalendar.)
    /// </summary>
    internal sealed class IcsTransfer
    {
        private const string SuppressExportWarningKey = "SuppressPlaintextExportWarning";

        private readonly ICalendarHost _host;
        private readonly EventStore _store;
        private readonly Action _afterChange;

        internal IcsTransfer(ICalendarHost host, EventStore store, Action afterChange)
        {
            _host = host;
            _store = store;
            _afterChange = afterChange;
        }

        internal void Import()
        {
            // Themed picker rather than Microsoft.Win32.OpenFileDialog, which ignores the theme.
            var dlg = new FileDialog(FileDialogMode.Open)
            {
                Title = _host.Loc("Str_Dlg_ImportTitle"),
                Filter = _host.Loc("Str_Dlg_ImportFilter"),
                CheckFileExists = true
            };
            if (dlg.ShowDialog(_host.Window) != true) return;

            try
            {
                var ok = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
                {
                    ".csv" => ImportCsv(dlg.FileName),
                    ".eml" => ImportEml(dlg.FileName),
                    _ => ImportIcs(IcsService.ParseFile(dlg.FileName)),
                };
                if (ok) _afterChange();
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_Status_ImportFailed"), ex.Message));
            }
        }

        private bool ImportIcs(IcsParseResult parsed)
        {
            int added   = _store.ImportEvents(parsed.Events);
            int skipped = parsed.Events.Count - added;

            var status = new StringBuilder(skipped > 0
                ? string.Format(_host.Loc("Str_Status_ImportedSkipped"), added, skipped)
                : string.Format(_host.Loc("Str_Status_Imported"), added));

            // Everything the file contained that Killendar could not keep is SAID, not
            // swallowed. A repeat rule too exotic to draw in particular imports as a single
            // date, and the user has no way of discovering that except by missing the second
            // occurrence.
            Note(status, parsed.Repeating,   "Str_Status_ImportRepeat");
            Note(status, parsed.Unreadable,  "Str_Status_ImportUnreadable");
            Note(status, parsed.Unsupported, "Str_Status_ImportUnsupported");

            _host.SetStatus(status.ToString());
            return true;
        }

        private bool ImportCsv(string path)
        {
            var parsed = CsvService.ParseFile(path);

            // Not the Outlook column set: refused whole, with the expected columns named.
            // Guessing at arbitrary columns means guessing what "Date" means and getting it
            // wrong, which corrupts quietly - worse than a clear no.
            if (!parsed.HeaderValid)
            {
                _host.SetStatus(_host.Loc("Str_Status_CsvBadHeader"));
                return false;
            }

            int added   = _store.ImportEvents(parsed.Events);
            int skipped = parsed.Events.Count - added;

            var status = new StringBuilder(skipped > 0
                ? string.Format(_host.Loc("Str_Status_ImportedSkipped"), added, skipped)
                : string.Format(_host.Loc("Str_Status_Imported"), added));
            Note(status, parsed.Unreadable, "Str_Status_ImportUnreadable");

            _host.SetStatus(status.ToString());
            return true;
        }

        private bool ImportEml(string path)
        {
            string? ics = EmlService.ExtractCalendarText(path);
            if (ics == null)
            {
                _host.SetStatus(_host.Loc("Str_Status_NoInvite"));
                return false;
            }
            return ImportIcs(IcsService.ParseText(ics));
        }

        // ── Export: scope flyout, then the picker, then the format the extension names ──

        private Popup?        _scopePopup;
        private ToggleButton  _chipWhole = null!, _chipRange = null!;
        private TextBox       _fromBox   = null!, _toBox     = null!;
        private StackPanel    _rangeRow  = null!;
        private TextBlock     _rangeHint = null!;

        internal void Export()
        {
            if (_store.Events.Count == 0)
            {
                _host.SetStatus(_host.Loc("Str_Status_NothingToExport"));
                return;
            }

            if (_scopePopup == null) BuildScopePopup();

            // Defaults reset on every open: neither chip lit, range row hidden, range boxes
            // seeded with the current month so picking "Date range" starts from something
            // sensible rather than blanks.
            var today = DateTime.Today;
            var first = new DateTime(today.Year, today.Month, 1);
            _fromBox.Text = DateFormatManager.Format(first);
            _toBox.Text   = DateFormatManager.Format(first.AddMonths(1).AddDays(-1));
            _chipWhole.IsChecked  = false;
            _chipRange.IsChecked  = false;
            _rangeRow.Visibility  = Visibility.Collapsed;
            _rangeHint.Visibility = Visibility.Collapsed;

            // Top-left corner of the content pane, mirroring the rail flyouts' bottom-left -
            // one place, inside the window, clear of the toolbar (FlyoutPlacement.cs).
            FlyoutPlacement.AttachTopLeft(_scopePopup!);
            _scopePopup!.IsOpen = true;
        }

        private void BuildScopePopup()
        {
            // Same raised-surface pattern as every popup in the app: drop shadow on an item-free
            // sibling border, content border on top, grain over the surface.
            var shadow = new Border { CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
            shadow.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            shadow.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.5
            };

            var grain = new Border { CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
            grain.SetResourceReference(Border.BackgroundProperty, "GrainTileBrush");
            grain.SetResourceReference(UIElement.OpacityProperty, "GrainOpacity");

            // Two buttons, no separate confirm (Steve, 2026-07-31): "Whole calendar" goes
            // straight to the save dialog; "Date range" reveals the from/to boxes, and Enter in
            // either box proceeds - the dates are the only reason range cannot be one click too.
            _chipWhole = Chip("Str_Exp_Whole");
            _chipRange = Chip("Str_Exp_Range");
            _chipWhole.Click += (_, _) =>
            {
                _chipWhole.IsChecked = true;
                _chipRange.IsChecked = false;
                ConfirmWhole();
            };
            _chipRange.Click += (_, _) =>
            {
                _chipWhole.IsChecked = false;
                _chipRange.IsChecked = true;
                _rangeRow.Visibility = Visibility.Visible;
                _fromBox.Focus();
                _fromBox.SelectAll();
            };

            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            chips.Children.Add(_chipWhole);
            chips.Children.Add(_chipRange);

            _fromBox = DateBox();
            _toBox   = DateBox();
            _fromBox.KeyDown += RangeBox_KeyDown;
            _toBox.KeyDown   += RangeBox_KeyDown;
            _rangeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _rangeRow.Children.Add(FieldLabel("Str_Exp_From"));
            _rangeRow.Children.Add(_fromBox);
            _rangeRow.Children.Add(FieldLabel("Str_Exp_To"));
            _rangeRow.Children.Add(_toBox);

            _rangeHint = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _rangeHint.SetResourceReference(TextBlock.ForegroundProperty, "DangerRed");
            _rangeHint.SetResourceReference(TextBlock.TextProperty, "Str_Exp_BadRange");

            var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            panel.Children.Add(chips);
            panel.Children.Add(_rangeRow);
            panel.Children.Add(_rangeHint);

            var content = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1)
            };
            content.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            content.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");
            var inner = new Grid();
            inner.Children.Add(grain);
            inner.Children.Add(panel);
            content.Child = inner;

            var host = new Grid { Margin = new Thickness(8) };
            host.Children.Add(shadow);
            host.Children.Add(content);

            _scopePopup = new Popup
            {
                Child = host,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 4,
                StaysOpen = false,
                AllowsTransparency = true
            };
        }

        private static ToggleButton Chip(string key)
        {
            var chip = new ToggleButton { Margin = new Thickness(0, 0, 6, 0) };
            chip.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
            chip.SetResourceReference(ContentControl.ContentProperty, key);
            return chip;
        }

        private static TextBox DateBox()
        {
            var box = new TextBox
            {
                Width = 96,
                Height = 26,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                ToolTip = DateFormatManager.Hint
            };
            box.SetResourceReference(FrameworkElement.StyleProperty, "DarkTextBox");
            return box;
        }

        private static TextBlock FieldLabel(string key)
        {
            var lbl = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            lbl.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
            lbl.SetResourceReference(TextBlock.TextProperty, key);
            return lbl;
        }

        private void ConfirmWhole()
        {
            WholeCalendarSpan(out var from, out var toEx);
            _scopePopup!.IsOpen = false;
            RunExport(from, toEx, whole: true);
        }

        private void RangeBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            ConfirmRange();
        }

        private void ConfirmRange()
        {
            if (!DateFormatManager.TryParse(_fromBox.Text.Trim(), out var f) ||
                !DateFormatManager.TryParse(_toBox.Text.Trim(),   out var t) ||
                t.Date < f.Date)
            {
                _rangeHint.Visibility = Visibility.Visible;
                return;
            }

            _scopePopup!.IsOpen = false;
            RunExport(f.Date, t.Date.AddDays(1), whole: false);
        }

        private void RunExport(DateTime from, DateTime toEx, bool whole)
        {
            if (!ConfirmPlaintextExport()) return;

            var dlg = new FileDialog(FileDialogMode.Save)
            {
                Title = _host.Loc("Str_Dlg_ExportTitle"),
                Filter = _host.Loc("Str_Dlg_ExportFilter"),
                FileName = $"killendar-{DateTime.Today:yyyy-MM-dd}.ics",
                DefaultExt = ".ics"
            };
            if (dlg.ShowDialog(_host.Window) != true) return;

            try
            {
                int count;
                switch (Path.GetExtension(dlg.FileName).ToLowerInvariant())
                {
                    // CSV and HTML get EXPANDED occurrences - one row or chip per date, which is
                    // what a spreadsheet reader or a printed month expects. GetInRange does the
                    // expansion, so overrides and skipped dates behave exactly as on screen.
                    case ".csv":
                    {
                        var rows = _store.GetInRange(from, toEx);
                        CsvService.ExportToFile(rows, dlg.FileName);
                        count = rows.Count;
                        break;
                    }

                    case ".htm":
                    case ".html":
                    {
                        var rows = _store.GetInRange(from, toEx);
                        HtmlExport.ExportToFile(rows, from, toEx, dlg.FileName);
                        count = rows.Count;
                        break;
                    }

                    // .ics keeps series as RULES, not expanded rows - that is the format's whole
                    // point, and the receiving calendar rebuilds the dates itself.
                    default:
                    {
                        var evs = whole ? [.. _store.Events] : SelectForIcs(from, toEx);
                        IcsService.ExportToFile(evs, dlg.FileName);
                        count = evs.Count;
                        break;
                    }
                }

                _host.SetStatus(string.Format(_host.Loc("Str_Status_Exported"), count));
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_Status_ExportFailed"), ex.Message));
            }
        }

        /// <summary>
        /// Which STORED rows a ranged .ics export carries. A series is included WHOLE when any of
        /// its occurrences falls in the range - clipping an RRULE to a window means rewriting the
        /// rule, and a rewritten rule that disagrees with the original is worse than a file that
        /// carries a few dates beyond the window. A series travels with all its overrides, even
        /// ones outside the range: an override is part of the series' truth, and dropping it
        /// resurrects the date it replaced.
        /// </summary>
        private List<CalendarEvent> SelectForIcs(DateTime from, DateTime toEx)
        {
            var masters = new HashSet<Guid>();
            foreach (var e in _store.Events)
                if (e.IsSeries && Recurrence.Expand(e, from, toEx).Any())
                    masters.Add(e.Id);

            var result = new List<CalendarEvent>();
            foreach (var e in _store.Events)
            {
                if (e.IsSeries)       { if (masters.Contains(e.Id)) result.Add(e); }
                else if (e.IsOverride) { if (masters.Contains(e.SeriesKey) ||
                                             (e.Start < toEx && e.End > from)) result.Add(e); }
                else                  { if (e.Start < toEx && e.End > from) result.Add(e); }
            }
            return result;
        }

        /// <summary>
        /// The span "whole calendar" means for the formats that need real dates (CSV, HTML).
        /// First appointment to last, where "last" for a series is its final occurrence - worked
        /// out exactly for a count- or date-limited series, and capped at ONE YEAR from today for
        /// a series that never ends, because "everything" of a forever-series has no last row.
        /// </summary>
        private void WholeCalendarSpan(out DateTime from, out DateTime toEx)
        {
            var today = DateTime.Today;
            from = today;
            var last  = today;

            foreach (var e in _store.Events)
            {
                if (e.Start.Date < from) from = e.Start.Date;

                if (!e.IsSeries)
                {
                    if (e.End.Date > last) last = e.End.Date;
                    continue;
                }

                if (e.RepeatCount <= 0 && !e.RepeatUntil.HasValue)
                {
                    var cap = today.AddYears(1);
                    if (cap > last) last = cap;
                    continue;
                }

                // Bounded series: expand to its real final occurrence. COUNT bounds the work by
                // itself and UNTIL bounds the range, so this stays cheap.
                var far = e.Start < DateTime.MaxValue.AddYears(-101) ? e.Start.AddYears(100) : DateTime.MaxValue;
                foreach (var occ in Recurrence.Expand(e, e.Start, far))
                    if (occ.End.Date > last) last = occ.End.Date;
            }

            toEx = last.AddDays(1);
        }

        /// <summary>
        /// Appends one caveat sentence to the import status when its count is non-zero. No
        /// separator is added here: each locale's string carries its OWN leading punctuation,
        /// because the sentence break is "." in Latin scripts, "。" in Japanese and Chinese and
        /// "।" in Bengali. Hardcoding ". " would put a Western full stop in every language.
        /// </summary>
        private void Note(StringBuilder status, int count, string key)
        {
            if (count <= 0) return;
            status.Append(string.Format(_host.Loc(key), count));
        }

        /// <summary>
        /// An .ics file is plain text, so exporting from an encrypted Killendar hands out everything
        /// the password was protecting. Warned before the picker - the question is whether to export
        /// at all, not where to put it - with a "Don't remind me again" box. A plaintext Killendar
        /// never sees this: there is nothing to undo. False only when the user cancels.
        /// </summary>
        private bool ConfirmPlaintextExport()
        {
            if (!_store.HasPassword) return true;
            if (Settings.Get(SuppressExportWarningKey) == "1") return true;

            var dlg = new ConfirmDialog(
                _host.Loc("Str_Exp_WarnHead"),
                _host.Loc("Str_Exp_WarnBody"),
                _host.Loc("Str_Btn_Export"),
                _host.Loc("Str_Btn_Cancel"),
                check1Label: _host.Loc("Str_Exp_WarnCheck")) { Owner = _host.Window };
            dlg.ShowDialog();

            // Only remember the choice if the export actually went ahead. Ticking the box and then
            // cancelling means "not this time", not "never warn me again".
            if (!dlg.Confirmed) return false;
            if (dlg.Check1Checked) Settings.Set(SuppressExportWarningKey, "1");
            return true;
        }
    }
}
