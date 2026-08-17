using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Killendar.Controls
{
    public partial class FileDialog
    {
        // ── Quick places (pinned + drives) ───────────────────────────────────────

        /// <summary>
        /// Pinned folders first (persisted, user-editable via right-click), then the ready
        /// drives. Drives are enumerated live every build - they come and go with USB sticks -
        /// and are not pinned, so they carry no remove menu.
        /// </summary>
        private void BuildPlaces()
        {
            Places.Clear();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in PinnedPaths())
                if (added.Add(p.TrimEnd('\\'))) AddPlace(LabelFor(p), p, pinned: true);

            foreach (var place in ExplorerQuickAccessPlaces())
                if (added.Add(place.Path.TrimEnd('\\'))) AddPlace(place.Label, place.Path);

            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                string label;
                try { label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel.Trim(); }
                catch { label = d.DriveType.ToString(); }
                if (added.Add(d.RootDirectory.FullName.TrimEnd('\\')))
                    AddPlace($"{d.Name.TrimEnd('\\')}  {label}", d.RootDirectory.FullName);
            }
        }

        /// <summary>Explorer's current Quick Access folders. Reflection keeps the COM automation
        /// call out of the runtime-binder dependency this net48 app deliberately does not carry.</summary>
        private static IEnumerable<(string Label, string Path)> ExplorerQuickAccessPlaces()
        {
            const string quickAccess = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";
            object? shell = null, folder = null, items = null;
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) yield break;
                shell = Activator.CreateInstance(type);
                folder = type.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell,
                                           [quickAccess]);
                if (folder == null) yield break;
                var folderType = folder.GetType();
                items = folderType.InvokeMember("Items", BindingFlags.InvokeMethod, null, folder, null);
                if (items == null) yield break;
                var itemsType = items.GetType();
                int count = Convert.ToInt32(itemsType.InvokeMember(
                    "Count", BindingFlags.GetProperty, null, items, null));
                for (int i = 0; i < count; i++)
                {
                    object? item = null;
                    try
                    {
                        item = itemsType.InvokeMember("Item", BindingFlags.InvokeMethod, null, items, [i]);
                        if (item == null) continue;
                        var itemType = item.GetType();
                        if (!Convert.ToBoolean(itemType.InvokeMember(
                                "IsFolder", BindingFlags.GetProperty, null, item, null))) continue;
                        string path = Convert.ToString(itemType.InvokeMember(
                            "Path", BindingFlags.GetProperty, null, item, null)) ?? "";
                        string name = Convert.ToString(itemType.InvokeMember(
                            "Name", BindingFlags.GetProperty, null, item, null)) ?? "";
                        if (Directory.Exists(path))
                            yield return (name.Length > 0 ? name : LabelFor(path), path);
                    }
                    finally { if (item != null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item); }
                }
            }
            finally
            {
                if (items != null && Marshal.IsComObject(items)) Marshal.FinalReleaseComObject(items);
                if (folder != null && Marshal.IsComObject(folder)) Marshal.FinalReleaseComObject(folder);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        /// <summary>
        /// The persisted pin list. First run (key absent, null) seeds the five standard folders;
        /// an EMPTY stored value means the user unpinned everything and must stay empty.
        /// </summary>
        private static List<string> PinnedPaths()
        {
            string? saved = Services.ThemeManager.GetSetting(PinnedKey);
            if (saved != null)
                return [.. saved.Split('|').Where(s => s.Length > 0)];

            return [.. new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            }.Where(p => !string.IsNullOrEmpty(p))];
        }

        /// <summary>Localized label for the five standard folders, plain folder name otherwise.</summary>
        private static string LabelFor(string path)
        {
            string p = path.TrimEnd('\\');
            bool Is(string other) => other.Length > 0 &&
                p.Equals(other.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

            if (Is(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))  return Loc("Str_QA_Home");
            if (Is(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))      return Loc("Str_QA_Desktop");
            if (Is(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)))  return Loc("Str_QA_Documents");
            if (Is(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")))
                                                                                       return Loc("Str_QA_Downloads");
            if (Is(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)))   return Loc("Str_QA_Pictures");

            var name = Path.GetFileName(p);
            return name.Length == 0 ? p : name;
        }

        private void AddPlace(string label, string path, bool pinned = false)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                Places.Add(new PickerPlace(label, path, pinned));
        }

        private void PinPlace(string path)
        {
            var list = PinnedPaths();
            if (list.Any(p => p.TrimEnd('\\').Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                return;
            list.Add(path);
            Services.ThemeManager.SetSetting(PinnedKey, string.Join("|", list));
            BuildPlaces();
            SyncPlacesSelection();
        }

        private PickerPlace? _placesMenuPlace;

        private void Places_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            _placesMenuPlace = ItemUnder<PickerPlace>(e.OriginalSource as DependencyObject);
            // Drives are dynamic, not pinned - nothing to remove; empty space likewise.
            if (_placesMenuPlace is not { Pinned: true }) e.Handled = true;
        }

        private void UnpinPlace_Click(object sender, RoutedEventArgs e)
        {
            if (_placesMenuPlace is not { Pinned: true } pl) return;
            var list = PinnedPaths()
                .Where(p => !p.TrimEnd('\\').Equals(pl.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                .ToList();
            Services.ThemeManager.SetSetting(PinnedKey, string.Join("|", list));
            BuildPlaces();
            SyncPlacesSelection();
        }

        private PickerEntry? _filesMenuEntry;

        private void Files_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            _filesMenuEntry = ItemUnder<PickerEntry>(e.OriginalSource as DependencyObject);
            if (_filesMenuEntry is not { IsFolder: true }) e.Handled = true;   // only folders pin
        }

        private void FilePin_Click(object sender, RoutedEventArgs e)
        {
            if (_filesMenuEntry is { IsFolder: true } en) PinPlace(en.FullPath);
        }

        /// <summary>Marks the place matching the current folder, or clears the marker.</summary>
        private void SyncPlacesSelection()
        {
            bool was = _navigating;
            _navigating = true;
            PlacesList.SelectedItem = _currentDir.Length == 0 ? null : Places.FirstOrDefault(p =>
                p.Path.TrimEnd('\\').Equals(_currentDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            _navigating = was;
        }

        /// <summary>The row model under a right-click, resolved by walking up to the ListBoxItem.</summary>
        private static T? ItemUnder<T>(DependencyObject? d) where T : class
        {
            while (d != null)
            {
                if (d is ListBoxItem lbi) return lbi.DataContext as T;
                d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        private static string Loc(string key)
            => Application.Current.TryFindResource(key) as string ?? key;
    }
}
