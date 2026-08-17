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
    /// <summary>Open or Save. Picked at construction; changes the accept button and the rules.</summary>
    public enum FileDialogMode { Open, Save, Folder }

    /// <summary>
    /// Themed stand-in for Microsoft.Win32.OpenFileDialog / SaveFileDialog. Same chrome, places
    /// rail, view modes and sortable columns as FolderPickerDialog (row styles shared from
    /// Controls.xaml), plus a file name box and a filter combo.
    ///
    /// The property surface mirrors the Win32 dialogs on purpose - Title, Filter, FilterIndex,
    /// FileName, InitialDirectory, DefaultExt, AddExtension, OverwritePrompt, CheckFileExists -
    /// so adopting it at a call site is a one-word change:
    ///
    ///     var dlg = new FileDialog(FileDialogMode.Save) { Title = ..., Filter = ..., FileName = ... };
    ///     if (dlg.ShowDialog(owner) == true) Use(dlg.FileName);
    ///
    /// Multiselect is deliberately NOT implemented yet - nothing in the app needs it, and
    /// a half-working Multiselect is worse than an absent one. Add it when something wants it.
    /// </summary>
    public partial class FileDialog : Window
    {
        // ── Win32-compatible surface ─────────────────────────────────────────────

        /// <summary>Win32 filter syntax: "Desc|*.a;*.b|Other|*.c". Empty means every file.</summary>
        public string Filter { get; set; } = "";

        /// <summary>1-based, like the Win32 dialogs. Out of range is clamped.</summary>
        public int FilterIndex { get; set; } = 1;

        /// <summary>Seeded with a suggested name; on OK, the full chosen path.</summary>
        public string FileName { get; set; } = "";

        public string InitialDirectory { get; set; } = "";

        /// <summary>Appended on save when the typed name has no extension. No leading dot needed.</summary>
        public string DefaultExt { get; set; } = "";

        public bool AddExtension { get; set; } = true;

        /// <summary>Save mode: confirm before replacing an existing file.</summary>
        public bool OverwritePrompt { get; set; } = true;

        /// <summary>Open mode: refuse to return a path that does not exist.</summary>
        public bool CheckFileExists { get; set; } = true;

        // ── internals ────────────────────────────────────────────────────────────

        private readonly FileDialogMode _mode;

        public ObservableCollection<PickerPlace> Places  { get; } = [];
        public ObservableCollection<PickerEntry> Entries { get; } = [];

        private readonly List<PickerEntry> _raw = [];
        private string _currentDir = string.Empty;
        private bool _navigating;
        private bool _built;                 // suppresses filter events during construction
        private int  _viewMode;              // 0 list, 1 icons, 2 details
        private int  _sortKey;               // 0 name, 1 size, 2 modified
        private bool _sortAsc = true;

        // Per-filter-entry patterns, parallel to FilterCombo's items. Empty list = show all.
        private readonly List<string[]> _filterPatterns = [];

        private static readonly string ArrowUp   = ((char)0xE70E).ToString();
        private static readonly string ArrowDown = ((char)0xE70D).ToString();

        // ── Tree / pinned places / recents / hidden state ────────────────────────
        public ObservableCollection<FolderNode> TreeRoots { get; } = [];
        private bool _treeSyncing;   // tree selection navigates, navigation selects: no ping-pong
        private bool _showHidden;

        private const string ShowHiddenKey = "FileDlgShowHidden";
        private const string RecentsKey    = "FileDlgRecents";
        private const string PinnedKey     = "FileDlgPinned";
        private const string PlacesHKey    = "FileDlgPlacesH";
        private const string LastOpenKey   = "FileDlgLastOpenDir";
        private const string LastSaveKey   = "FileDlgLastSaveDir";
        private const int    RecentsMax    = 12;

        public FileDialog(FileDialogMode mode = FileDialogMode.Open)
        {
            _mode = mode;
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);

            // Size and placement remembered separately from the folder picker: this dialog is a
            // different shape and sharing the keys would make each one fight the other.
            try
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (double.TryParse(Services.ThemeManager.GetSetting("FileDlgW"),
                        System.Globalization.NumberStyles.Float, ci, out double w) &&
                    double.TryParse(Services.ThemeManager.GetSetting("FileDlgH"),
                        System.Globalization.NumberStyles.Float, ci, out double h))
                {
                    Width  = Math.Max(MinWidth,  Math.Min(w, SystemParameters.WorkArea.Width));
                    Height = Math.Max(MinHeight, Math.Min(h, SystemParameters.WorkArea.Height));
                }
                if (double.TryParse(Services.ThemeManager.GetSetting("FileDlgX"),
                        System.Globalization.NumberStyles.Float, ci, out double x) &&
                    double.TryParse(Services.ThemeManager.GetSetting("FileDlgY"),
                        System.Globalization.NumberStyles.Float, ci, out double y))
                {
                    var wa = SystemParameters.WorkArea;
                    if (x > wa.Left - Width + 80 && x < wa.Right - 80 &&
                        y > wa.Top - 20 && y < wa.Bottom - 80)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Left = x;
                        Top  = y;
                    }
                }
                if (double.TryParse(Services.ThemeManager.GetSetting(PlacesHKey),
                        System.Globalization.NumberStyles.Float, ci, out double ph) && ph >= 56)
                    PlacesRow.Height = new GridLength(Math.Min(ph, 600));
            }
            catch { /* registry unavailable - defaults are fine */ }

            _showHidden = Services.ThemeManager.GetSetting(ShowHiddenKey) == "1";
            FolderNode.ShowHidden = _showHidden;
            ApplyShowHiddenButton();

            Closing += (_, _) =>
            {
                try
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    Services.ThemeManager.SetSetting("FileDlgW", ActualWidth.ToString(ci));
                    Services.ThemeManager.SetSetting("FileDlgH", ActualHeight.ToString(ci));
                    Services.ThemeManager.SetSetting("FileDlgX", Left.ToString(ci));
                    Services.ThemeManager.SetSetting("FileDlgY", Top.ToString(ci));
                    Services.ThemeManager.SetSetting(PlacesHKey, PlacesRow.ActualHeight.ToString(ci));
                }
                catch { /* not worth failing the close */ }
            };

            // NO DwmChrome calls, deliberately. On an AllowsTransparency window the DWM corner
            // preference makes DWM composite its own rounded frame around the WINDOW rect - the
            // transparent 10px halo included - and SetThemeBorder tints it: that WAS the gray
            // band. The other four dialogs are AllowsTransparency with no DWM calls and have
            // never shown one; the card draws its own border and shadow, so DWM has nothing to
            // add. The WM_ERASEBKGND hook that lived here went too - a layered window is rendered
            // via UpdateLayeredWindow and never receives it. (2026-07-30, fifth attempt)
        }

        /// <summary>
        /// Sets the owner and shows modally. Everything that depends on Filter / FileName /
        /// InitialDirectory is wired HERE rather than in the constructor, because callers set
        /// those as object-initializer properties after construction.
        /// </summary>
        public bool? ShowDialog(Window? owner)
        {
            if (owner != null && owner.IsVisible) Owner = owner;

            HeadingText.Text    = Title ?? "";
            AcceptButton.Content = Loc(_mode switch
            {
                FileDialogMode.Save => "Str_Btn_Save",
                FileDialogMode.Folder => "Str_Btn_Select",
                _ => "Str_Btn_Open",
            });
            FileFields.Visibility = _mode == FileDialogMode.Folder
                ? Visibility.Collapsed : Visibility.Visible;

            // Open mode has nothing to name, so the box is for typing/filtering a path, not a
            // new file. It stays visible: typing an exact name is faster than hunting for it.
            BuildFilters();
            BuildPlaces();
            PlacesList.ItemsSource = Places;
            FileList.ItemsSource   = Entries;
            InitTree();
            ApplyView();

            // A seeded FileName can be a bare name ("export.ics"), a full path, or empty.
            string startDir = InitialDirectory;
            string seedName = "";
            if (!string.IsNullOrWhiteSpace(FileName))
            {
                if (FileName.IndexOfAny(['\\', '/']) >= 0)
                {
                    var d = Path.GetDirectoryName(FileName);
                    if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) startDir = d!;
                    seedName = Path.GetFileName(FileName);
                }
                else seedName = FileName;
            }
            if (string.IsNullOrWhiteSpace(startDir) || !Directory.Exists(startDir))
            {
                string? remembered = Services.ThemeManager.GetSetting(
                    _mode == FileDialogMode.Open ? LastOpenKey : LastSaveKey);
                startDir = !string.IsNullOrWhiteSpace(remembered) && Directory.Exists(remembered)
                    ? remembered!
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            _built = true;
            NavigateTo(startDir);
            FileNameBox.Text = seedName;

            // Save: preselect the stem so typing replaces the name but keeps the extension
            // visible. Open: caret at the end.
            if (_mode == FileDialogMode.Folder) AcceptButton.Focus();
            else FileNameBox.Focus();
            if (_mode == FileDialogMode.Save && seedName.Length > 0)
            {
                int dot = seedName.LastIndexOf('.');
                FileNameBox.Select(0, dot > 0 ? dot : seedName.Length);
            }
            else FileNameBox.CaretIndex = FileNameBox.Text.Length;

            return ShowDialog();
        }
    }
}
