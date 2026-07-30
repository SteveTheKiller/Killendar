using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Services;

namespace Killendar
{
    // Manage Killendars: list every .kcal in the data folder with size / modified / [encrypted] /
    // [active] flags; create (+), delete (with confirm), load one from elsewhere, reveal in
    // Explorer, rename inline via double-click, or pick one to switch to (Selected, for the
    // caller). The store is CLOSED while this dialog is up, so file operations - the active file
    // included - are safe.
    //
    // Ported from KillerNotes' DatabasesDialog. Not carried over: the change-data-folder picker.
    // Killendars are per-user under %APPDATA% by design, and a folder picker would need
    // FolderPicker.cs ported as well for a feature nobody asked for.
    public partial class KillendarsDialog : Window
    {
        /// <summary>File name the user chose to open, or null if they just closed.</summary>
        public string? Selected { get; private set; }

        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        public KillendarsDialog()
        {
            InitializeComponent();

            // Segoe MDL2 glyphs assigned in code, never pasted into XAML: the private-use
            // characters do not survive tooling. Add / delete / bring-a-file-in / data folder.
            // E8DA points the arrow INTO the page; E8E5 was tried first and points out, which
            // reads as export. E838 is the folder KillerNotes' equivalent button uses.
            NewBtn.Content      = ((char)0xE710).ToString();
            DeleteBtn.Content   = ((char)0xE711).ToString();
            LoadBtn.Content     = ((char)0xE8DA).ToString();
            ExplorerBtn.Content = ((char)0xE838).ToString();

            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            RefreshList();
        }

        private void RefreshList(string? select = null)
        {
            KcList.Items.Clear();
            string active = EventStore.ActiveFile;

            foreach (string name in EventStore.ListKillendars())
            {
                string full = Path.Combine(EventStore.DataDir, name);
                var fi = new FileInfo(full);
                bool isActive = string.Equals(name, active, StringComparison.OrdinalIgnoreCase);
                bool enc = EventStore.IsEncryptedFile(full);

                // Name and metadata are separate TextBlocks so inline rename can swap just the
                // name part for a TextBox.
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var nameText = new TextBlock { Text = name, FontSize = 12 };
                var meta = new TextBlock
                {
                    Text = $"   {fi.Length / 1024:N0} KB   {fi.LastWriteTime:yyyy-MM-dd HH:mm}"
                         + (enc ? "   [" + Loc("Str_Kc_FlagEncrypted") + "]" : "")
                         + (isActive ? "   [" + Loc("Str_Kc_FlagActive") + "]" : ""),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                row.Children.Add(nameText);
                row.Children.Add(meta);

                var item = new ListBoxItem { Tag = name, Content = row };
                SetRowColors(nameText, meta, isActive, selected: false);
                item.Selected   += (_, _) => SetRowColors(nameText, meta, isActive, selected: true);
                item.Unselected += (_, _) => SetRowColors(nameText, meta, isActive, selected: false);
                KcList.Items.Add(item);

                if (select != null
                        ? string.Equals(name, select, StringComparison.OrdinalIgnoreCase)
                        : isActive)
                    KcList.SelectedItem = item;
            }
            DlgStatus.Text = EventStore.DataDir;
        }

        // A selected row fills with the accent (RowSelectedBrush); force white text so name and
        // meta stay readable - in the Light accents the fill and the accent text are the same hue,
        // which made a selected row unreadable in KillerNotes until this was added. Unselected:
        // the active Killendar's name in the accent, the rest normal. SetResourceReference rather
        // than a cached brush, so the colours follow a theme switch.
        private static void SetRowColors(TextBlock name, TextBlock meta, bool active, bool selected)
        {
            if (selected)
            {
                name.Foreground = Brushes.White;
                meta.Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                name.SetResourceReference(TextBlock.ForegroundProperty, active ? "PrimaryBrush" : "TextBrush");
                meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            }
        }

        private ListBoxItem? SelectedItem => KcList.SelectedItem as ListBoxItem;
        private string? SelectedFile => SelectedItem?.Tag as string;

        // ---- Inline rename (double-click the name, or right-click > Rename) ----

        private void KcList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedItem is ListBoxItem item) BeginRename(item);
        }

        /// <summary>Right-click selects the row under the cursor before the context menu opens.</summary>
        private void KcList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && !(d is ListBoxItem))
                d = VisualTreeHelper.GetParent(d);
            if (d is ListBoxItem item) item.IsSelected = true;
        }

        private void RenameMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem is ListBoxItem item) BeginRename(item);
        }

        private void BeginRename(ListBoxItem item)
        {
            if (!(item.Tag is string oldName) || !(item.Content is StackPanel row)) return;

            var box = new TextBox
            {
                Text = oldName,
                FontSize = 12,
                MinWidth = 180,
                Padding = new Thickness(2, 0, 2, 0),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent,
            };
            box.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
            box.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
            box.SetResourceReference(TextBox.BorderBrushProperty, "PrimaryBrush");
            box.SetResourceReference(TextBox.SelectionBrushProperty, "PrimaryBrush");
            box.SelectionOpacity = 0.35;

            bool done = false;   // guard: Enter commits, then LostFocus fires again
            void Finish(bool commit)
            {
                if (done) return;
                done = true;
                if (!commit || !TryRename(oldName, box.Text)) RefreshList(oldName);
            }
            box.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter) { Finish(true); ke.Handled = true; }
                else if (ke.Key == Key.Escape) { Finish(false); ke.Handled = true; }
            };
            box.LostFocus += (_, _) => Finish(true);

            row.Children.RemoveAt(0);          // the name TextBlock
            row.Children.Insert(0, box);
            box.Focus();
            // Preselect the stem without the extension, ready to overtype.
            int stem = oldName.LastIndexOf(EventStore.Extension, StringComparison.OrdinalIgnoreCase);
            box.Select(0, stem > 0 ? stem : oldName.Length);
        }

        /// <summary>Validates and applies a rename; refreshes the list on success.</summary>
        private bool TryRename(string oldName, string newNameRaw)
        {
            string newName = newNameRaw.Trim();
            if (newName.Length == 0) return false;
            if (!newName.EndsWith(EventStore.Extension, StringComparison.OrdinalIgnoreCase))
                newName += EventStore.Extension;
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return false;
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                DlgStatus.Text = Loc("Str_Kc_BadName");
                return false;
            }
            if (File.Exists(Path.Combine(EventStore.DataDir, newName)))
            {
                DlgStatus.Text = string.Format(Loc("Str_Kc_Exists"), newName);
                return false;
            }
            try
            {
                EventStore.RenameKillendar(oldName, newName);
                RefreshList(newName);
                DlgStatus.Text = string.Format(Loc("Str_Kc_Renamed"), oldName, newName);
                return true;
            }
            catch (Exception ex)
            {
                DlgStatus.Text = string.Format(Loc("Str_Kc_RenameFailed"), ex.Message);
                return false;
            }
        }

        // ---- New: auto-named, then straight into inline rename ----

        private void New_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = EventStore.CreateKillendar();
                RefreshList(name);
                if (SelectedItem is ListBoxItem item) BeginRename(item);
            }
            catch (Exception ex)
            {
                DlgStatus.Text = string.Format(Loc("Str_Kc_CreateFailed"), ex.Message);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!(SelectedFile is string name)) { DlgStatus.Text = Loc("Str_Kc_SelectFirst"); return; }

            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Kc_DeleteHead"), name),
                Loc("Str_Kc_DeleteBody"),
                Loc("Str_Btn_Delete"),
                Loc("Str_Btn_Cancel")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            try
            {
                EventStore.DeleteKillendar(name);
                // Deleting the active file leaves the setting pointing at nothing; retarget it so
                // the caller reopens something that exists rather than resurrecting the name.
                if (string.Equals(name, EventStore.ActiveFile, StringComparison.OrdinalIgnoreCase))
                {
                    var left = EventStore.ListKillendars();
                    EventStore.SetActive(left.Count > 0 ? left[0] : EventStore.DefaultFileName);
                }
                RefreshList();
                DlgStatus.Text = string.Format(Loc("Str_Kc_Deleted"), name);
            }
            catch (Exception ex)
            {
                DlgStatus.Text = string.Format(Loc("Str_Kc_DeleteFailed"), ex.Message);
            }
        }

        /// <summary>Load a .kcal from anywhere. It is COPIED into the data folder rather than
        /// opened in place: a Killendar is written to constantly, and writing into someone's
        /// Downloads folder or a network share is not what "Load" should mean.</summary>
        private void Load_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc("Str_Kc_LoadTitle"),
                Filter = Loc("Str_Kc_Filter"),
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                string name = EventStore.ImportKillendar(dlg.FileName);
                RefreshList(name);
                DlgStatus.Text = string.Format(Loc("Str_Kc_Loaded"), name);
            }
            catch (Exception ex)
            {
                DlgStatus.Text = string.Format(Loc("Str_Kc_LoadFailed"), ex.Message);
            }
        }

        /// <summary>Puts the .kcal on the clipboard as a real file drop, so pasting into Explorer,
        /// Teams or an email copies or attaches the file itself.</summary>
        private void CopyFileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (!(SelectedFile is string name)) { DlgStatus.Text = Loc("Str_Kc_SelectFirst"); return; }
            try
            {
                var files = new System.Collections.Specialized.StringCollection
                    { Path.Combine(EventStore.DataDir, name) };
                Clipboard.SetFileDropList(files);
                DlgStatus.Text = string.Format(Loc("Str_Kc_Copied"), name);
            }
            catch (Exception ex)
            {
                DlgStatus.Text = string.Format(Loc("Str_Kc_CopyFailed"), ex.Message);
            }
        }

        private void RevealMenu_Click(object sender, RoutedEventArgs e)
        {
            if (!(SelectedFile is string name)) { Explorer_Click(sender, e); return; }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe",
                    "/select,\"" + Path.Combine(EventStore.DataDir, name) + "\"") { UseShellExecute = true });
            }
            catch { /* best-effort */ }
        }

        private void Explorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(EventStore.DataDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", EventStore.DataDir) { UseShellExecute = true });
            }
            catch { /* best-effort */ }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (!(SelectedFile is string name)) { DlgStatus.Text = Loc("Str_Kc_SelectFirst"); return; }
            Selected = name;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}
