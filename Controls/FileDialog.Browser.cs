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
        private void Files_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_navigating) return;
            if (FileList.SelectedItem is PickerEntry en)
            {
                // Selecting a FILE fills the name box - that is the value being chosen. Selecting
                // a folder does not: it is a navigation target, and overwriting the typed name
                // with a folder name would lose what the user was in the middle of typing.
                if (!en.IsFolder) FileNameBox.Text = en.Name;
                SelName.Text = en.Name;
                SelMeta.Text = en.IsFolder ? en.ModifiedLabel : $"{en.SizeLabel}  |  {en.ModifiedLabel}";
            }
            else UpdateInfoSummary();
        }

        private void Files_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileList.SelectedItem is not PickerEntry en) return;
            if (en.IsFolder) NavigateTo(en.FullPath);
            else Accept();
        }

        private void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var typed = PathBox.Text?.Trim();
            if (!string.IsNullOrEmpty(typed) && Directory.Exists(typed)) NavigateTo(typed!);
            e.Handled = true;
        }

        private void FileNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;

            var typed = FileNameBox.Text?.Trim() ?? "";

            // A directory typed into the name box navigates instead of accepting - matches the
            // Win32 dialogs, and is how people paste a path in.
            if (typed.Length > 0)
            {
                var asDir = Path.IsPathRooted(typed) ? typed : Path.Combine(_currentDir, typed);
                if (Directory.Exists(asDir)) { NavigateTo(asDir); FileNameBox.Clear(); return; }
            }

            // A wildcard retargets the listing rather than naming a file.
            if (typed.IndexOfAny(['*', '?']) >= 0)
            {
                _filterPatterns.Insert(0, [typed]);
                FilterCombo.Items.Insert(0, typed);
                FilterCombo.SelectedIndex = 0;
                FileNameBox.Clear();
                return;
            }

            Accept();
        }

        private void UpdateInfoSummary()
        {
            int folders = _raw.Count(x => x.IsFolder);
            int shown   = Entries.Count(x => !x.IsFolder);
            var leaf    = Path.GetFileName(_currentDir.TrimEnd('\\'));
            SelName.Text = leaf.Length == 0 ? _currentDir : leaf;
            SelMeta.Text = string.Format(Loc("Str_Sum_Counts"), folders, shown);
        }

        // ── View modes ───────────────────────────────────────────────────────────

        private void ViewList_Click(object sender, RoutedEventArgs e)    => SetView(0);
        private void ViewIcons_Click(object sender, RoutedEventArgs e)   => SetView(1);
        private void ViewDetails_Click(object sender, RoutedEventArgs e) => SetView(2);

        private void SetView(int mode)
        {
            _viewMode = mode;
            ApplyView();
        }

        /// <summary>
        /// The three views differ in panel, template AND scroll direction - that last one is the
        /// part that is easy to miss. List view wraps into columns and scrolls sideways, which only
        /// works if vertical scrolling is DISABLED: an enabled vertical ScrollViewer hands the panel
        /// infinite height, so a vertical WrapPanel never wraps and you get one tall column.
        /// </summary>
        private void ApplyView()
        {
            switch (_viewMode)
            {
                case 1:  // icons: grid, wraps across, scrolls down
                    FileList.ItemsPanel   = (ItemsPanelTemplate)FindResource("PanelIconGrid");
                    FileList.ItemTemplate = (DataTemplate)FindResource("IconTemplate");
                    ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Disabled);
                    ScrollViewer.SetVerticalScrollBarVisibility(FileList, ScrollBarVisibility.Auto);
                    break;

                case 2:  // details: one row per entry, scrolls down
                    FileList.ItemsPanel   = (ItemsPanelTemplate)FindResource("PanelStack");
                    FileList.ItemTemplate = (DataTemplate)FindResource("DetailsTemplate");
                    ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Disabled);
                    ScrollViewer.SetVerticalScrollBarVisibility(FileList, ScrollBarVisibility.Auto);
                    break;

                default: // list: columns of small icons, scrolls RIGHT
                    FileList.ItemsPanel   = (ItemsPanelTemplate)FindResource("PanelListCols");
                    FileList.ItemTemplate = (DataTemplate)FindResource("RowTemplate");
                    ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Auto);
                    ScrollViewer.SetVerticalScrollBarVisibility(FileList, ScrollBarVisibility.Disabled);
                    break;
            }

            DetailsHeader.Visibility = _viewMode == 2 ? Visibility.Visible : Visibility.Collapsed;
            ViewListBtn.Tag    = _viewMode == 0 ? "on" : null;
            ViewIconsBtn.Tag   = _viewMode == 1 ? "on" : null;
            ViewDetailsBtn.Tag = _viewMode == 2 ? "on" : null;
        }

        /// <summary>List view wraps into columns and scrolls horizontally, so translate a normal
        /// vertical wheel notch to that scrollbar. Icon and details views keep native vertical
        /// scrolling.</summary>
        private void FileList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewMode != 0) return;
            var sv = FindDescendant<ScrollViewer>(FileList);
            if (sv is null) return;
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        // ── Sorting ──────────────────────────────────────────────────────────────

        private void SortName_Click(object sender, RoutedEventArgs e)     => SetSort(0);
        private void SortSize_Click(object sender, RoutedEventArgs e)     => SetSort(1);
        private void SortModified_Click(object sender, RoutedEventArgs e) => SetSort(2);

        private void SetSort(int key)
        {
            if (_sortKey == key) _sortAsc = !_sortAsc;
            else { _sortKey = key; _sortAsc = true; }
            ApplySort();
        }

        /// <summary>
        /// Rebuilds Entries from _raw: filter applied, folders always before files, then the
        /// active sort key. Folders-first is not a sort key - it is the frame the sort runs in.
        /// </summary>
        private void ApplySort()
        {
            var visible = _raw.Where(PassesFilter);

            IOrderedEnumerable<PickerEntry> ordered = _sortKey switch
            {
                1 => _sortAsc ? visible.OrderBy(x => !x.IsFolder).ThenBy(x => x.SizeBytes)
                              : visible.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.SizeBytes),
                2 => _sortAsc ? visible.OrderBy(x => !x.IsFolder).ThenBy(x => x.Modified)
                              : visible.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.Modified),
                _ => _sortAsc ? visible.OrderBy(x => !x.IsFolder).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                              : visible.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase),
            };

            Entries.Clear();
            foreach (var e in ordered) Entries.Add(e);

            NameArrow.Text = _sortKey == 0 ? (_sortAsc ? ArrowUp : ArrowDown) : "";
            SizeArrow.Text = _sortKey == 1 ? (_sortAsc ? ArrowUp : ArrowDown) : "";
            ModArrow.Text  = _sortKey == 2 ? (_sortAsc ? ArrowUp : ArrowDown) : "";

            EmptyHint.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
