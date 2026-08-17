using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Killendar.Controls
{
    public partial class FileDialog
    {
        // ── Filters ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses Win32 filter syntax into the combo plus a parallel pattern list. A malformed
        /// filter (odd number of segments) degrades to "all files" rather than throwing - a bad
        /// filter string should not stop someone opening a file.
        /// </summary>
        private void BuildFilters()
        {
            FilterCombo.Items.Clear();
            _filterPatterns.Clear();

            var parts = (Filter ?? "").Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                var label = parts[i].Trim();
                var pats  = parts[i + 1].Split(';')
                                        .Select(p => p.Trim())
                                        .Where(p => p.Length > 0)
                                        .ToArray();
                if (label.Length == 0 || pats.Length == 0) continue;
                FilterCombo.Items.Add(label);
                _filterPatterns.Add(pats);
            }

            if (FilterCombo.Items.Count == 0)
            {
                FilterCombo.Items.Add(Loc("Str_Dlg_AllFiles"));
                _filterPatterns.Add(["*.*"]);
            }

            int idx = FilterIndex - 1;
            FilterCombo.SelectedIndex = idx >= 0 && idx < FilterCombo.Items.Count ? idx : 0;
            FilterLabel.Visibility = FilterCombo.Visibility =
                FilterCombo.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_built) return;
            FilterIndex = FilterCombo.SelectedIndex + 1;

            // Save mode follows the Win32 dialogs: switching the type swaps the typed name's
            // extension - but only when the current one belongs to another entry of THIS filter.
            // An extension the user typed by hand is theirs and is left alone.
            if (_mode == FileDialogMode.Save)
            {
                string? newExt = ActiveFilterExt();
                string  name   = FileNameBox.Text?.Trim() ?? "";
                string  cur    = name.Length == 0 ? "" : Path.GetExtension(name);
                if (newExt != null && cur.Length > 0 &&
                    !cur.Equals(newExt, StringComparison.OrdinalIgnoreCase) &&
                    AllFilterExts().Contains(cur, StringComparer.OrdinalIgnoreCase))
                {
                    FileNameBox.Text = Path.ChangeExtension(name, newExt);
                }
            }

            ApplySort();
        }

        /// <summary>
        /// The active filter entry's own extension (".csv"), or null when its first pattern is a
        /// wildcard-any or a multi-pattern catch-all that names no single extension.
        /// </summary>
        private string? ActiveFilterExt()
        {
            int i = FilterCombo.SelectedIndex;
            if (i < 0 || i >= _filterPatterns.Count) return null;
            string p = _filterPatterns[i][0];
            if (p.Length > 2 && p.StartsWith("*.") && p.IndexOfAny(['*', '?'], 2) < 0)
                return p.Substring(1);
            return null;
        }

        /// <summary>Every concrete extension the filter list names, for the swap test above.</summary>
        private IEnumerable<string> AllFilterExts()
        {
            foreach (var pats in _filterPatterns)
                foreach (var p in pats)
                    if (p.Length > 2 && p.StartsWith("*.") && p.IndexOfAny(['*', '?'], 2) < 0)
                        yield return p.Substring(1);
        }

        /// <summary>True when the name passes the active filter. Folders are never filtered out.</summary>
        private bool PassesFilter(PickerEntry en)
        {
            if (en.IsFolder) return true;
            int i = FilterCombo.SelectedIndex;
            if (i < 0 || i >= _filterPatterns.Count) return true;
            var pats = _filterPatterns[i];
            return pats.Any(p => p == "*.*" || p == "*" || WildcardMatch(en.Name, p));
        }

        /// <summary>Case-insensitive glob. Anchored, so "*.ics" does not match "a.icsx".</summary>
        private static bool WildcardMatch(string name, string pattern)
        {
            var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
