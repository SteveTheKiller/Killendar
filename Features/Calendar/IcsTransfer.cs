using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private const int WholeExportOccurrenceLimit = 50000;

        private readonly ICalendarHost _host;
        private readonly EventStore _store;

        internal IcsTransfer(ICalendarHost host, EventStore store)
        {
            _host = host;
            _store = store;
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

            Import(dlg.FileName);
        }

        /// <summary>
        /// Reads, plans and writes an import. Reading and writing run on a worker thread while the
        /// host waits, so a large file never freezes the window and can be canceled; nothing is
        /// written until the whole file has been read and, when something in it would be skipped
        /// or lost, the user has agreed. A canceled or failed import changes nothing.
        /// </summary>
        internal void Import(string path)
        {
            try
            {
                using var cancel = new CancellationTokenSource();
                string extension = Path.GetExtension(path).ToLowerInvariant();

                var reading = Task.Run(() => Read(path, extension, cancel.Token), cancel.Token);
                if (_host.WaitOrCancel(string.Format(_host.Loc("Str_Imp_Reading"), Path.GetFileName(path)), reading))
                    cancel.Cancel();
                ImportSource source;
                try { source = reading.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { Stopped(); return; }

                if (source.Refusal != null)
                {
                    _host.SetStatus(_host.Loc(source.Refusal));
                    return;
                }

                // Back on the window's thread: compare against the calendar as it is now.
                if (source.Csv)
                {
                    var importable = CsvService.Importable(_store, source.Events);
                    source.AlreadyPresent = source.Events.Count - importable.Count;
                    source.Events = importable;
                }
                var plan = _store.PlanImport(source.Events);
                plan.AlreadyPresent += source.AlreadyPresent;
                if (plan.Fresh.Count > 0 && source.Losses(plan) &&
                    !_host.ConfirmImport(string.Format(_host.Loc("Str_Imp_PreviewHead"), plan.Fresh.Count), Preview(source, plan)))
                {
                    Stopped();
                    return;
                }

                if (plan.Fresh.Count > 0)
                {
                    var writing = Task.Run(() => _store.WriteImport(plan, cancel.Token), cancel.Token);
                    if (_host.WaitOrCancel(string.Format(_host.Loc("Str_Imp_Saving"), plan.Fresh.Count), writing))
                        cancel.Cancel();
                    try { writing.GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { Stopped(); return; }
                    _store.PublishImport(plan);
                }

                _host.SetStatus(Summary(source, plan));
            }
            catch (ImportLimitException)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_Status_ImportTooLarge"),
                    ImportLimits.MaxBytes / (1024 * 1024), ImportLimits.MaxEvents));
            }
            catch (Exception ex)
            {
                _host.SetStatus(string.Format(_host.Loc("Str_Status_ImportFailed"), ex.Message));
            }
        }

        private void Stopped() => _host.SetStatus(_host.Loc("Str_Status_ImportStopped"));

        /// <summary>What a file yielded, whichever format it was. Runs on a worker thread, so it
        /// never touches the store: the window keeps running while it reads, and a command-line
        /// request can change the calendar in the meantime.</summary>
        private ImportSource Read(string path, string extension, CancellationToken cancel)
        {
            switch (extension)
            {
                case ".csv":
                {
                    var parsed = CsvService.ParseFile(path, cancel);
                    // Not the Outlook column set: refused whole, with the expected columns named.
                    // Guessing at arbitrary columns means guessing what "Date" means and getting
                    // it wrong, which corrupts quietly - worse than a clear no.
                    if (!parsed.HeaderValid) return new ImportSource { Refusal = "Str_Status_CsvBadHeader" };
                    return new ImportSource
                    {
                        Events = parsed.Events,
                        Csv = true,
                        Unreadable = parsed.Unreadable,
                        Malformed = parsed.Malformed,
                    };
                }
                case ".eml":
                {
                    string? ics = EmlService.ExtractCalendarText(path);
                    if (ics == null) return new ImportSource { Refusal = "Str_Status_NoInvite" };
                    return ImportSource.From(IcsService.ParseText(ics, cancel));
                }
                default:
                    return ImportSource.From(IcsService.ParseFile(path, cancel));
            }
        }

        private string Preview(ImportSource source, ImportPlan plan)
        {
            var lines = new List<string>();
            void Line(int count, string key)
            {
                if (count > 0) lines.Add(string.Format(_host.Loc(key), count));
            }
            Line(plan.AlreadyPresent, "Str_Imp_PvDuplicates");
            Line(source.Canceled, "Str_Imp_PvCanceled");
            Line(source.LostDetails, "Str_Imp_PvLost");
            Line(source.Repeating, "Str_Imp_PvRepeat");
            Line(source.Unreadable + source.Malformed + source.Unsupported, "Str_Imp_PvSkipped");
            return string.Join("\n", lines);
        }

        private string Summary(ImportSource source, ImportPlan plan)
        {
            int added = plan.Fresh.Count;
            var status = new StringBuilder(plan.AlreadyPresent > 0
                ? string.Format(_host.Loc("Str_Status_ImportedSkipped"), added, plan.AlreadyPresent)
                : string.Format(_host.Loc("Str_Status_Imported"), added));

            // Everything the file contained that Killendar could not keep is SAID, not
            // swallowed. A repeat rule too exotic to draw in particular imports as a single
            // date, and the user has no way of discovering that except by missing the second
            // occurrence.
            Note(status, source.Repeating,   "Str_Status_ImportRepeat");
            Note(status, source.Unreadable,  "Str_Status_ImportUnreadable");
            Note(status, source.Malformed,   "Str_Status_ImportMalformed");
            Note(status, source.Unsupported - source.UnsupportedRanges, "Str_Status_ImportUnsupported");
            Note(status, source.UnsupportedRanges, "Str_Status_ImportUnsupportedRanges");
            Note(status, source.Canceled, "Str_Status_ImportCanceled");
            Note(status, source.LostDetails, "Str_Status_ImportLostDetails");
            return status.ToString();
        }

        private sealed class ImportSource
        {
            internal List<CalendarEvent> Events = [];
            internal string? Refusal;
            internal bool Csv;
            internal int AlreadyPresent, Repeating, Unreadable, Malformed, Unsupported, UnsupportedRanges, Canceled, LostDetails;

            internal static ImportSource From(IcsParseResult parsed) => new()
            {
                Events = parsed.Events,
                Repeating = parsed.Repeating,
                Unreadable = parsed.Unreadable,
                Unsupported = parsed.Unsupported,
                UnsupportedRanges = parsed.UnsupportedRanges,
                Canceled = parsed.Canceled,
                LostDetails = parsed.LostDetails,
            };

            /// <summary>Anything the user would not get back by importing this file.</summary>
            internal bool Losses(ImportPlan plan) => plan.AlreadyPresent + Repeating + Unreadable + Malformed +
                                                     Unsupported + Canceled + LostDetails > 0;
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

            // Two buttons, no separate confirm (2026-07-31): "Whole calendar" goes
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
            // First, because Whole calendar exports on the click: the web page's layout and what
            // private details it carries are chosen before (HtmlOptions, remembered).
            panel.Children.Add(PageOptions());
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

        /// <summary>Layout and private details for an .html export, saved as they change.</summary>
        private StackPanel PageOptions()
        {
            var options = HtmlOptions.Parse(Settings.Get(HtmlOptions.Setting));
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var head = FieldLabel("Str_Exp_PageHead");
            head.Margin = new Thickness(0, 0, 0, 4);
            section.Children.Add(head);

            var layouts = new WrapPanel();
            foreach (var (layout, key) in new[] { (HtmlLayout.Month, "Str_View_Month"), (HtmlLayout.Week, "Str_View_Week"),
                                                  (HtmlLayout.Day, "Str_View_Day"), (HtmlLayout.Agenda, "Str_View_Agenda") })
            {
                var choice = new RadioButton { GroupName = "HtmlLayout", IsChecked = options.Layout == layout, Margin = new Thickness(0, 0, 12, 2) };
                choice.SetResourceReference(ContentControl.ContentProperty, key);
                choice.SetResourceReference(FrameworkElement.StyleProperty, "ThemeRadio");
                choice.Checked += (_, _) => { options.Layout = layout; Settings.Set(HtmlOptions.Setting, options.ToSetting()); };
                layouts.Children.Add(choice);
            }
            section.Children.Add(layouts);

            foreach (var (key, get, set) in new (string, Func<bool>, Action<bool>)[]
            {
                ("Str_Exp_IncLocations", () => options.Locations, v => options.Locations = v),
                ("Str_Exp_IncDescriptions", () => options.Descriptions, v => options.Descriptions = v),
                ("Str_Exp_IncAttendees", () => options.Attendees, v => options.Attendees = v),
            })
            {
                var tick = new CheckBox { IsChecked = get(), Margin = new Thickness(0, 2, 0, 0) };
                tick.SetResourceReference(ContentControl.ContentProperty, key);
                tick.Click += (_, _) => { set(tick.IsChecked == true); Settings.Set(HtmlOptions.Setting, options.ToSetting()); };
                section.Children.Add(tick);
            }
            return section;
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
            _scopePopup!.IsOpen = false;
            RunExport(DateTime.Today, DateTime.Today.AddDays(1), whole: true);
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
            RunExport(f.Date, t.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : t.Date.AddDays(1), whole: false);
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
                string format = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                if (whole && (format == ".csv" || format == ".htm" || format == ".html"))
                {
                    WholeCalendarSpan(out from, out toEx);
                    if (_store.Events.Any(e => e.IsSeries && e.RepeatCount <= 0 && !e.RepeatUntil.HasValue))
                    {
                        var lastDay = toEx == DateTime.MaxValue ? toEx.Date : toEx.AddDays(-1).Date;
                        var warning = new ConfirmDialog(_host.Loc("Str_Dlg_ExportTitle"),
                            string.Format(_host.Loc("Str_Exp_ForeverRange"),
                                DateFormatManager.Format(from), DateFormatManager.Format(lastDay)),
                            _host.Loc("Str_Btn_Export"), _host.Loc("Str_Btn_Cancel")) { Owner = _host.Window };
                        warning.ShowDialog();
                        if (!warning.Confirmed) return;
                    }
                }
                int count;
                switch (format)
                {
                    // CSV and HTML get EXPANDED occurrences - one row or chip per date, which is
                    // what a spreadsheet reader or a printed month expects. GetInRange does the
                    // expansion, so overrides and skipped dates behave exactly as on screen.
                    case ".csv":
                    {
                        var rows = _store.GetInRange(from, toEx);
                        if (CsvService.RequiresSpreadsheetProtection(rows))
                        {
                            var warning = new ConfirmDialog(_host.Loc("Str_Dlg_ExportTitle"),
                                _host.Loc("Str_Exp_CsvSafety"), _host.Loc("Str_Btn_Export"),
                                _host.Loc("Str_Btn_Cancel")) { Owner = _host.Window };
                            warning.ShowDialog();
                            if (!warning.Confirmed) return;
                        }
                        CsvService.ExportToFile(rows, dlg.FileName);
                        count = rows.Count;
                        break;
                    }

                    case ".htm":
                    case ".html":
                    {
                        var rows = _store.GetInRange(from, toEx);
                        HtmlExport.ExportToFile(rows, from, toEx, dlg.FileName, HtmlOptions.Parse(Settings.Get(HtmlOptions.Setting)));
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
        internal List<CalendarEvent> SelectForIcs(DateTime from, DateTime toEx)
        {
            var masters = new HashSet<Guid>();
            foreach (var e in _store.Events)
                if (e.IsSeries && Recurrence.Expand(e, from, toEx).Any())
                    masters.Add(e.Id);
                else if (e.IsOverride && e.Start < toEx && e.End > from)
                    masters.Add(e.SeriesKey);

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
        /// out exactly for a count- or date-limited series. A never-ending series contributes
        /// at least its first year, and its chosen export range is confirmed before publication.
        /// </summary>
        internal void WholeCalendarSpan(out DateTime from, out DateTime toEx)
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
                    var anchor = e.Start.Date > today ? e.Start.Date : today;
                    var cap = anchor.Year == 9999 ? DateTime.MaxValue.Date : anchor.AddYears(1);
                    if (cap > last) last = cap;
                    continue;
                }

                int count = 0;
                foreach (var occ in Recurrence.Expand(e, e.Start, DateTime.MaxValue))
                {
                    if (++count > WholeExportOccurrenceLimit) RequireExportRange();
                    if (occ.End.Date > last) last = occ.End.Date;
                }
            }

            toEx = last == DateTime.MaxValue.Date ? DateTime.MaxValue : last.AddDays(1);
            // The combined span can extend another series beyond its individual horizon.
            // Bound the total work before GetInRange materializes the flattened export.
            int total = 0;
            foreach (var e in _store.Events.Where(e => e.IsSeries))
                foreach (var occurrence in Recurrence.Expand(e, from, toEx))
                    if (++total > WholeExportOccurrenceLimit) RequireExportRange();
        }

        private void RequireExportRange()
            => throw new InvalidOperationException(string.Format(
                _host?.Loc("Str_Exp_RangeRequired") ?? "Whole calendar export supports up to {0} recurring appointments. Choose a date range.",
                WholeExportOccurrenceLimit.ToString("N0")));

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
            // canceling means "not this time", not "never warn me again".
            if (!dlg.Confirmed) return false;
            if (dlg.Check1Checked) Settings.Set(SuppressExportWarningKey, "1");
            return true;
        }
    }
}
