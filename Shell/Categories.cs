using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Controls;
using Killendar.Services;

namespace Killendar.Shell
{
    /// <summary>
    /// Categories in the shell: the rail's Manage button, and the assignment chips in the
    /// appointment panel.
    ///
    /// Assignment is by clicking chips rather than typing, because the names come from the
    /// Killendar's own definitions - a free-text box would let a typo become an orphan that
    /// renders in neutral gray forever. Chips are built in code because their colors are user
    /// data, and so must NOT go on with SetResourceReference (they must not follow a theme
    /// switch, unlike every themed brush in the views).
    /// </summary>
    public partial class MainWindow
    {
        private readonly HashSet<string> _selectedCategories =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // ---- Manage dialog ----

        private void CategoriesButton_Click(object sender, RoutedEventArgs e) => OpenCategoriesDialog();

        internal void OpenCategoriesDialog()
        {
            // Definitions live IN the Killendar, so there is nothing to manage until one is
            // open - during an unlock prompt, for instance.
            if (!_store.IsOpen) return;
            var dlg = new CategoriesDialog(_store) { Owner = this };
            // The panel may be open on an appointment while the dialog renames or deletes a
            // category, so its chips are rebuilt from what is now defined. Selections survive
            // by name; a renamed one falls back to the orphan path below rather than vanishing.
            dlg.CategoriesChanged += () => BuildCategoryChips(ReadCategoryChips());
            dlg.ShowDialog();
        }

        // ---- Assignment chips ----

        /// <summary>Rebuilds the chip row for an assignment string, preserving what it names.</summary>
        private void BuildCategoryChips(string assigned)
        {
            _selectedCategories.Clear();
            foreach (string name in EventStore.SplitCategories(assigned)) _selectedCategories.Add(name);

            FieldCategories.Children.Clear();
            var defined = CategoryManager.Order;

            // Anything assigned but no longer defined still gets a chip, so opening an imported
            // appointment and saving it does not silently strip categories this Killendar has
            // never heard of. CategoryManager paints those in the orphan gray.
            var names = defined.Select(d => d.Name).ToList();
            foreach (string name in _selectedCategories)
                if (!names.Any(n => string.Equals(n, name, System.StringComparison.OrdinalIgnoreCase)))
                    names.Add(name);

            foreach (string name in names) FieldCategories.Children.Add(CategoryChipButton(name));

            bool none = names.Count == 0;
            FieldCategoriesEmpty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            FieldCategories.Visibility      = none ? Visibility.Collapsed : Visibility.Visible;

            // With no categories defined there are no chips to right-click, so the empty-state
            // line is the way in.
            FieldCategoriesEmpty.Cursor = Cursors.Hand;
            FieldCategoriesEmpty.MouseLeftButtonUp  -= OpenCategoriesFromHint;
            FieldCategoriesEmpty.MouseRightButtonUp -= OpenCategoriesFromHint;
            FieldCategoriesEmpty.MouseLeftButtonUp  += OpenCategoriesFromHint;
            FieldCategoriesEmpty.MouseRightButtonUp += OpenCategoriesFromHint;
        }

        private void OpenCategoriesFromHint(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            OpenCategoriesDialog();
        }

        /// <summary>The assignment string the editor stores: definition order, orphans last.</summary>
        private string ReadCategoryChips() =>
            string.Join(", ", FieldCategories.Children.OfType<Border>()
                .Where(b => b.Tag is string s && _selectedCategories.Contains(s))
                .Select(b => (string)b.Tag!));

        private Border CategoryChipButton(string name)
        {
            var chip = new Border
            {
                CornerRadius    = new CornerRadius(3),
                Padding         = new Thickness(7, 2, 7, 2),
                Margin          = new Thickness(0, 0, 4, 4),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
                Tag             = name,
                Child           = new TextBlock { Text = name, FontSize = 11 }
            };
            PaintCategoryChip(chip, name);
            chip.MouseLeftButtonUp += (_, _) =>
            {
                if (!_selectedCategories.Remove(name)) _selectedCategories.Add(name);
                PaintCategoryChip(chip, name);
            };
            // Right-click a chip to manage the definitions: left-click assigns, right-click edits
            // the thing being assigned.
            chip.MouseRightButtonUp += (_, e) => { e.Handled = true; OpenCategoriesDialog(); };
            return chip;
        }

        // Selected reads as the category filled solid; unselected as its outline only, so the
        // color is visible either way and the state is unambiguous at a glance.
        private void PaintCategoryChip(Border chip, string name)
        {
            var color = CategoryManager.ColorOf(name);
            var fill  = CategoryManager.BrushOf(name);
            bool on   = _selectedCategories.Contains(name);
            var text  = (TextBlock)chip.Child;

            chip.BorderBrush = fill;
            if (on)
            {
                chip.Background = fill;
                text.Foreground = CategoryManager.ForegroundFor(color);
            }
            else
            {
                chip.Background = Brushes.Transparent;
                text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            }
        }
    }
}
