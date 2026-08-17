using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>
    /// Manage categories: add / rename / recolor / delete the open Killendar's definitions.
    /// Rename and delete ripple through every appointment's assignment string (EventStore).
    /// The store stays OPEN, unlike Manage Killendars - these are ordinary row edits on the
    /// live database. Ported from KillerNotes' TagsDialog.
    /// </summary>
    public partial class CategoriesDialog : Window
    {
        // Row action glyphs: E790 paint roller and E74D delete. Built from codepoints,
        // NEVER pasted as literal private-use characters - a literal PUA glyph does not survive
        // tooling, which is the same rule the sidebar rail chevrons follow. This file must stay
        // 0 non-ASCII bytes.
        private static readonly string GlyphRecolor = ((char)0xE790).ToString();
        private static readonly string GlyphDelete  = ((char)0xE74D).ToString();

        private readonly EventStore _store;
        private string _newColor = "#50AEE8";   // default pick for the add row

        /// <summary>Raised after every add/rename/recolor/delete so the owner can repaint the
        /// views LIVE, without waiting for the dialog to close.</summary>
        public event Action? CategoriesChanged;

        private static string Loc(string key) => LocaleManager.Loc(key);

        // Re-fills the dialog's own list AND notifies the owner (live repaint).
        private void Changed(string? select = null)
        {
            Refresh(select);
            CategoriesChanged?.Invoke();
        }

        public CategoriesDialog(EventStore store)
        {
            _store = store;
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            NewColorSwatch.Background = BrushFromHex(_newColor);
            Refresh();
        }

        private void Refresh(string? select = null)
        {
            CategoryList.Items.Clear();
            foreach (var def in _store.ListCategories())
                CategoryList.Items.Add(BuildRow(def.Name, def.Color));
            if (select != null)
                foreach (ListBoxItem item in CategoryList.Items)
                    if ((string)item.Tag == select) { item.IsSelected = true; break; }
        }

        // swatch + directly editable name + [recolor] [delete], all carrying the category name.
        private ListBoxItem BuildRow(string name, string colorHex)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                Background = BrushFromHex(colorHex), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 10, 0),
            };
            grid.Children.Add(swatch);

            var label = new TextBlock
            {
                Text = name, VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.IBeam, ToolTip = Loc("Str_TT_CatRename")
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(actions, 2);
            actions.Children.Add(RowButton(GlyphRecolor, Loc("Str_TT_CatRecolor"), () => RecolorCategory(name)));
            actions.Children.Add(RowButton(GlyphDelete,  Loc("Str_TT_CatDelete"),  () => DeleteCategory(name)));
            grid.Children.Add(actions);

            var row = new ListBoxItem { Content = grid, Tag = name, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            // The name itself is the rename affordance. A deliberate click on it edits in place;
            // double-click and F2 remain available for keyboard/Explorer familiarity.
            label.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                row.IsSelected = true;
                BeginRename(grid, label, name);
            };
            row.MouseDoubleClick += (_, _) => BeginRename(grid, label, name);

            var menu = new ContextMenu();
            var miRename = new MenuItem
            {
                Header = Loc("Str_TT_CatRename"), InputGestureText = "F2",
                Icon = Views.CalendarChrome.MenuGlyph(0xE70F)
            };
            miRename.Click += (_, _) => BeginRename(grid, label, name);
            var miColor = new MenuItem
            {
                Header = Loc("Str_TT_CatRecolor"),
                Icon = Views.CalendarChrome.MenuGlyph(0xE790)
            };
            miColor.Click += (_, _) => RecolorCategory(name);
            var miDelete = new MenuItem
            {
                Header = Loc("Str_TT_CatDelete"), InputGestureText = "Delete",
                Icon = Views.CalendarChrome.MenuGlyph(0xE74D)
            };
            miDelete.Click += (_, _) => DeleteCategory(name);
            menu.Items.Add(miRename);
            menu.Items.Add(miColor);
            menu.Items.Add(miDelete);
            row.ContextMenu = menu;
            return row;
        }

        private Button RowButton(string glyph, string tip, Action onClick)
        {
            var b = new Button
            {
                Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                Width = 26, Height = 22, Margin = new Thickness(2, 0, 0, 0), Padding = new Thickness(0),
                ToolTip = tip, Style = TryFindResource("SurfaceButton") as Style,
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        private void CategoryList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (CategoryList.SelectedItem is not ListBoxItem row ||
                row.Content is not Grid grid || row.Tag is not string name)
                return;

            if (e.Key == Key.F2 && grid.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            {
                BeginRename(grid, label, name);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteCategory(name);
                e.Handled = true;
            }
        }

        // ---- Add ----

        private void NewColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new ColorPickerDialog(this, ColorFromHex(_newColor)) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _newColor = HexFromColor(dlg.SelectedColor);
                NewColorSwatch.Background = new SolidColorBrush(dlg.SelectedColor);
            }
        }

        private void NewNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Add_Click(sender, e);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            // Commas separate the names inside an appointment's assignment string, so a name
            // may not contain one.
            string name = NewNameBox.Text.Replace(",", "").Trim();
            if (name.Length == 0) { DlgStatus.Text = Loc("Str_Cat_NeedName"); return; }
            if (Exists(name)) { DlgStatus.Text = Loc("Str_Cat_Exists"); return; }

            _store.AddCategory(name, _newColor);
            NewNameBox.Text = "";
            DlgStatus.Text = "";
            Changed(select: name);
        }

        private bool Exists(string name) =>
            _store.ListCategories().Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        // ---- Per-row actions ----

        private void RecolorCategory(string name)
        {
            string cur = _store.ListCategories()
                .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Color).FirstOrDefault() ?? "#50AEE8";
            var dlg = new ColorPickerDialog(this, ColorFromHex(cur)) { Owner = this };
            // Live preview, the way KillerNotes previews a group title color: every drag of the
            // picker repaints this dialog's swatch AND the appointments on the calendar behind it.
            // Nothing is written until OK, and Cancel puts the original color back.
            dlg.ColorChanged += c =>
            {
                string hex = HexFromColor(c);
                CategoryManager.Preview(name, hex);
                PreviewRowSwatch(name, c);
            };
            if (dlg.ShowDialog() == true)
            {
                _store.SetCategoryColor(name, HexFromColor(dlg.SelectedColor));
                Changed(select: name);
            }
            else
            {
                CategoryManager.Preview(name, cur);
                PreviewRowSwatch(name, ColorFromHex(cur));
            }
        }

        /// <summary>Repaints one row's swatch in place. The rows are built from the database, so a
        /// preview - which deliberately never reaches the database - has to be painted directly.</summary>
        private void PreviewRowSwatch(string name, Color color)
        {
            foreach (ListBoxItem item in CategoryList.Items)
            {
                if ((string)item.Tag != name) continue;
                if (item.Content is Grid g && g.Children.Count > 0 && g.Children[0] is Border swatch)
                    swatch.Background = new SolidColorBrush(color);
                break;
            }
        }

        // Inline rename: swap the row's label for a TextBox, the same pattern Manage Killendars
        // uses. Commit on Enter or lost focus; Esc cancels.
        private void BeginRename(Grid grid, TextBlock label, string name)
        {
            // DarkTextBox is FontSize 14 with Padding 8,6, which does not fit a 22px row: the
            // content host fills top-down and the text line scrolls out of sight, leaving what
            // looks like an empty box. The style's own template comment describes this. So the
            // row overrides all three - smaller text, flat vertical padding, centered content.
            var box = new TextBox
            {
                Text = name, Height = 22,
                Style = TryFindResource("DarkTextBox") as Style,
                FontSize = 12,
                Padding = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(box, 1);
            label.Visibility = Visibility.Collapsed;
            grid.Children.Add(box);
            box.Focus();
            box.SelectAll();

            bool done = false;
            void Commit(bool apply)
            {
                if (done) return;
                done = true;
                grid.Children.Remove(box);
                label.Visibility = Visibility.Visible;
                if (apply) CommitRename(name, box.Text);
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit(true);
                else if (e.Key == Key.Escape) Commit(false);
            };
            box.LostFocus += (_, _) => Commit(true);
        }

        private void CommitRename(string oldName, string raw)
        {
            string next = raw.Replace(",", "").Trim();
            if (next.Length == 0 || string.Equals(next, oldName, StringComparison.OrdinalIgnoreCase)) return;
            if (Exists(next)) { DlgStatus.Text = Loc("Str_Cat_Exists"); return; }

            _store.RenameCategory(oldName, next);
            Changed(select: next);
        }

        private void DeleteCategory(string name)
        {
            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Cat_DeleteHead"), name),
                Loc("Str_Cat_DeleteBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;
            _store.DeleteCategory(name);
            Changed();
        }

        // ---- Color helpers ----

        private static Brush BrushFromHex(string hex)
        {
            try { return new SolidColorBrush(ColorFromHex(hex)); }
            catch { return Brushes.Gray; }
        }

        private static Color ColorFromHex(string hex) =>
            ColorPickerDialog.TryParseHex(hex, out Color c) ? c : Colors.Gray;

        private static string HexFromColor(Color c) =>
            $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // ---- Chrome ----

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
