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
        // ── Navigation ───────────────────────────────────────────────────────────

        private void NavigateTo(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

            _navigating = true;
            _currentDir  = dir;
            PathBox.Text = dir;
            _raw.Clear();

            try
            {
                // The toggle gates two things together: attribute Hidden/System AND leading-dot
                // names - the Unix convention is all over a Windows home folder (.gradle, .ssh)
                // and those carry no Hidden attribute. Same gate in the folder tree (FolderTree.cs).
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    DirectoryInfo info;
                    try { info = new DirectoryInfo(sub); } catch { continue; }
                    if (!_showHidden)
                    {
                        if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        if (info.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                    }
                    _raw.Add(new PickerEntry(info.Name, sub, true, 0, SafeTime(() => info.LastWriteTime)));
                }
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    FileInfo fi;
                    try { fi = new FileInfo(file); } catch { continue; }
                    if (!_showHidden)
                    {
                        if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        if (fi.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                    }
                    _raw.Add(new PickerEntry(fi.Name, file, false, SafeLen(fi), SafeTime(() => fi.LastWriteTime)));
                }
            }
            catch { /* unauthorized / unreadable - show what we have */ }

            ApplySort();
            UpButton.IsEnabled = Directory.GetParent(dir) != null;
            UpdateInfoSummary();
            SyncPlacesSelection();
            _navigating = false;

            RecordRecent(dir);
            _ = RevealInTree(dir);
        }

        private static DateTime SafeTime(Func<DateTime> get)
        {
            try { return get(); } catch { return DateTime.MinValue; }
        }

        private static long SafeLen(FileInfo fi)
        {
            try { return fi.Length; } catch { return 0; }
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            var parent = Directory.GetParent(_currentDir);
            if (parent != null) NavigateTo(parent.FullName);
        }

        private void Places_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_navigating) return;
            if (PlacesList.SelectedItem is PickerPlace p) NavigateTo(p.Path);
        }

        // ── Folder tree (ported from KillerShell, see Controls/FolderTree.cs) ────

        /// <summary>Ready drives only - an empty optical drive or a dropped mapping would sit
        /// there as a node that throws the moment anyone touches it.</summary>
        private void InitTree()
        {
            if (TreeRoots.Count > 0) return;
            FolderTreeCtl.ItemsSource = TreeRoots;

            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch (IOException) { return; }

            foreach (var d in drives)
            {
                bool ready;
                try { ready = d.IsReady; }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (ready) TreeRoots.Add(new FolderNode(d));
            }

            // Edge fades follow the scroll position (KillerShell TreePanel.cs). ScrollChanged is
            // handled at the TreeView rather than dug out of its template: it bubbles, so the
            // inner ScrollViewer is reached without needing to have found it first. Loaded and
            // SizeChanged cover the passes where nothing scrolled but the extent moved.
            FolderTreeCtl.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => { SyncTreeEdgeFades(); SyncTreeFade(); }));
            FolderTreeCtl.SizeChanged += (_, _) => { SyncTreeEdgeFades(); SyncTreeFade(); };
            FolderTreeCtl.Loaded      += (_, _) => { SyncTreeEdgeFades(); SyncTreeFade(); };

            // The places list gets the same treatment (2026-07-30). No scrollbar lift:
            // horizontal scrolling is disabled on it.
            PlacesList.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => SyncPlacesEdgeFades()));
            PlacesList.SizeChanged += (_, _) => SyncPlacesEdgeFades();
            PlacesList.Loaded      += (_, _) => SyncPlacesEdgeFades();
        }

        /// <summary>Places-list twin of SyncTreeEdgeFades: same ramp, same rules.</summary>
        private void SyncPlacesEdgeFades()
        {
            var sv = FindDescendant<ScrollViewer>(PlacesList);
            if (sv == null) return;

            PlacesFadeTop.Opacity    = Ramp(sv.VerticalOffset, PlacesFadeTop.Height, 18);
            PlacesFadeBottom.Opacity = Ramp(sv.ExtentHeight - sv.ViewportHeight - sv.VerticalOffset,
                                            PlacesFadeBottom.Height, 22);
        }

        /// <summary>
        /// Fade each edge only while there is something PAST it, ramped over the fade's own
        /// height: none at the very top, none at the very bottom, full in between. A proportional
        /// ramp rather than a flip - at one pixel of scroll it is one pixel's worth of fade, so
        /// neither edge ever pops. (KillerShell TreePanel.SyncTreeEdgeFades, verbatim.)
        /// </summary>
        private void SyncTreeEdgeFades()
        {
            var sv = FindDescendant<ScrollViewer>(FolderTreeCtl);
            if (sv == null) return;

            TreeFadeTop.Opacity    = Ramp(sv.VerticalOffset, TreeFadeTop.Height, 18);
            TreeFadeBottom.Opacity = Ramp(sv.ExtentHeight - sv.ViewportHeight - sv.VerticalOffset,
                                          TreeFadeBottom.Height, 22);
        }

        // Height is NaN until the border has been laid out, hence the fallback.
        private static double Ramp(double distance, double height, double fallback)
        {
            double h = double.IsNaN(height) || height <= 0 ? fallback : height;
            return Math.Min(1, Math.Max(0, distance) / h);
        }

        /// <summary>
        /// Keep the bottom edge fade sitting on the tree's last visible ROW rather than on the
        /// horizontal scrollbar underneath it. The bar's real height is measured, not taken from
        /// SystemParameters - the themed template is not the system metric. Base 4 is the tree's
        /// own bottom margin. (KillerShell TreePanel.SyncTreeFade, adapted.)
        /// </summary>
        private void SyncTreeFade()
        {
            var sv = FindDescendant<ScrollViewer>(FolderTreeCtl);
            double lift = 0;

            if (sv != null && sv.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
            {
                var bar = FindHorizontalBar(sv);
                lift = bar?.ActualHeight ?? SystemParameters.HorizontalScrollBarHeight;
            }

            var m = TreeFadeBottom.Margin;
            double want = 4 + lift;
            if (Math.Abs(m.Bottom - want) < 0.5) return;     // no churn on every layout pass
            TreeFadeBottom.Margin = new Thickness(m.Left, m.Top, m.Right, want);
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is T hit) return hit;
                var deeper = FindDescendant<T>(c);
                if (deeper != null) return deeper;
            }
            return null;
        }

        // FindDescendant takes the FIRST match of a type, and a ScrollViewer has two scrollbars,
        // so the orientation has to be checked rather than assumed.
        private static System.Windows.Controls.Primitives.ScrollBar? FindHorizontalBar(DependencyObject root)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is System.Windows.Controls.Primitives.ScrollBar sb &&
                    sb.Orientation == Orientation.Horizontal) return sb;
                var deeper = FindHorizontalBar(c);
                if (deeper != null) return deeper;
            }
            return null;
        }

        // TreeViewItem.Expanded is attached at the TreeView, so this fires for every node at any
        // depth - which is the point: one handler drives the whole lazy load.
        private async void FolderTree_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not TreeViewItem tvi) return;
            if (tvi.DataContext is not FolderNode node) return;
            await node.LoadChildrenAsync();
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_treeSyncing) return;
            if (e.NewValue is not FolderNode node) return;
            if (string.IsNullOrEmpty(node.Path)) return;   // the placeholder, mid-load
            NavigateTo(node.Path);
        }

        private FolderNode? _treeMenuNode;

        private void FolderTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            _treeMenuNode = NodeUnder(Mouse.DirectlyOver as DependencyObject)
                         ?? NodeUnder(e.OriginalSource as DependencyObject);
            // Drives are already in places; empty space has nothing to pin.
            if (_treeMenuNode == null || _treeMenuNode.IsDrive) e.Handled = true;
        }

        private void TreePin_Click(object sender, RoutedEventArgs e)
        {
            if (_treeMenuNode is { IsDrive: false } n && !string.IsNullOrEmpty(n.Path))
                PinPlace(n.Path);
        }

        private static FolderNode? NodeUnder(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is TreeViewItem tvi) return tvi.DataContext as FolderNode;
                d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        /// <summary>
        /// Points the tree at a folder reached from somewhere else - places, the path box, a
        /// double-click. Expands the chain of ANCESTORS and selects the folder; the destination's
        /// own expander is left exactly as the user had it (KillerShell's rule - forcing it
        /// collapsed the branch under the cursor and the whole tree jumped).
        /// </summary>
        private async Task RevealInTree(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;

            string full;
            try { full = Path.GetFullPath(folder); }
            catch { return; }

            var root = TreeRoots.FirstOrDefault(
                r => full.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase));
            if (root == null) return;

            var segments = RelativeSegments(root.Path, full).ToList();

            var current = root;
            if (segments.Count > 0)
            {
                await current.LoadChildrenAsync();
                current.IsExpanded = true;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                var next = current.Children.FirstOrDefault(
                    c => string.Equals(c.Name, segments[i], StringComparison.OrdinalIgnoreCase));
                if (next == null) return;   // hidden by the filter, or gone since the listing

                current = next;
                if (i == segments.Count - 1) break;

                await current.LoadChildrenAsync();   // needed to match the NEXT segment
                current.IsExpanded = true;
            }

            _treeSyncing = true;
            current.IsSelected = true;
            _treeSyncing = false;
        }

        private static IEnumerable<string> RelativeSegments(string rootPath, string fullPath)
        {
            string rest = fullPath.Substring(rootPath.Length);
            return rest.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                              StringSplitOptions.RemoveEmptyEntries);
        }

        // ── Recent locations ─────────────────────────────────────────────────────

        private static List<string> LoadRecents()
            => [.. (Services.ThemeManager.GetSetting(RecentsKey) ?? "")
               .Split('|').Where(s => s.Length > 0)];

        private static void RecordRecent(string dir)
        {
            var list = LoadRecents();
            list.RemoveAll(p => p.TrimEnd('\\').Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            list.Insert(0, dir);
            if (list.Count > RecentsMax) list.RemoveRange(RecentsMax, list.Count - RecentsMax);
            Services.ThemeManager.SetSetting(RecentsKey, string.Join("|", list));
        }

        private void RecentsBtn_Click(object sender, RoutedEventArgs e)
        {
            // Stale entries (unplugged drive, deleted folder) are filtered at open rather than
            // scrubbed from the store - the drive may be back tomorrow.
            var list = LoadRecents().Where(Directory.Exists).ToList();
            if (list.Count == 0) return;

            _navigating = true;              // rebinding must not raise a navigation
            RecentsList.ItemsSource = list;
            RecentsList.SelectedItem = null;
            _navigating = false;
            RecentsPopup.IsOpen = true;
        }

        private void RecentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_navigating) return;
            if (RecentsList.SelectedItem is not string dir) return;
            RecentsPopup.IsOpen = false;
            NavigateTo(dir);
        }

        // ── Hidden / dot files ───────────────────────────────────────────────────

        private void ShowHidden_Click(object sender, RoutedEventArgs e)
        {
            _showHidden = !_showHidden;
            Services.ThemeManager.SetSetting(ShowHiddenKey, _showHidden ? "1" : "0");
            FolderNode.ShowHidden = _showHidden;
            ApplyShowHiddenButton();

            if (_currentDir.Length > 0) NavigateTo(_currentDir);

            // Re-enumerate loaded tree branches in place, keeping expansion (FolderTree.cs).
            foreach (var r in TreeRoots.ToList()) _ = r.RefreshAsync();
        }

        private void ApplyShowHiddenButton()
        {
            // E7B3 eye at rest, E890 while showing - KillerShell's build-proven pair
            // (ViewOptions.cs). Codepoints, never literal PUA glyphs (family rule).
            ShowHiddenBtn.Content = ((char)(_showHidden ? 0xE890 : 0xE7B3)).ToString();
            ShowHiddenBtn.Tag     = _showHidden ? "on" : null;
        }
    }
}
