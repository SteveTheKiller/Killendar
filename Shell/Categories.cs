using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            new(System.StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _categoryOrder = [];

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
            _categoryOrder.Clear();
            foreach (string name in EventStore.SplitCategories(assigned))
                if (_selectedCategories.Add(name)) _categoryOrder.Add(name);

            FieldCategories.Children.Clear();
            var defined = CategoryManager.Order;

            // Anything assigned but no longer defined still gets a chip, so opening an imported
            // appointment and saving it does not silently strip categories this Killendar has
            // never heard of. CategoryManager paints those in the orphan gray.
            var names = defined.Select(d => d.Name).ToList();
            foreach (string name in _categoryOrder)
                if (!names.Any(n => string.Equals(n, name, System.StringComparison.OrdinalIgnoreCase)))
                    names.Add(name);

            foreach (string name in names) FieldCategories.Children.Add(CategoryChipButton(name));

            // With no categories defined there are no chips to right-click, so a hint button in the
            // chip row is the way in. It is a real Button so the keyboard and screen readers reach it.
            if (names.Count == 0)
                FieldCategories.Children.Add(CategoryChips.CreateHint(OpenCategoriesDialog));
            FieldCategoriesEmpty.Visibility = Visibility.Collapsed;
            FieldCategories.Visibility      = Visibility.Visible;
        }

        /// <summary>Preserves assignment order because the first category colors the appointment.</summary>
        private string ReadCategoryChips() => string.Join(", ", _categoryOrder);

        private ToggleButton CategoryChipButton(string name)
        {
            var chip = CategoryChips.Create(name, () =>
            {
                CategoryChips.Toggle(_selectedCategories, _categoryOrder, name);
                RefreshCategoryChips();
            });
            PaintCategoryChip(chip, name);
            var menu = new ContextMenu();
            var primary = new MenuItem();
            primary.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Str_Cat_MakePrimary");
            primary.Click += (_, _) => BuildCategoryChips(EventStore.WithPrimaryCategory(ReadCategoryChips(), name));
            menu.Items.Add(primary);
            menu.Items.Add(new Separator());
            var manage = new MenuItem();
            manage.SetResourceReference(HeaderedItemsControl.HeaderProperty, "Str_Cat_Title");
            manage.Click += (_, _) => OpenCategoriesDialog();
            menu.Items.Add(manage);
            chip.ContextMenu = menu;
            return chip;
        }

        private void RefreshCategoryChips()
        {
            foreach (ToggleButton chip in FieldCategories.Children.OfType<ToggleButton>())
                PaintCategoryChip(chip, (string)chip.Tag);
        }

        private void PaintCategoryChip(ToggleButton chip, string name) =>
            CategoryChips.Paint(chip, name, _selectedCategories.Contains(name), _categoryOrder);
    }

    /// <summary>
    /// Builds the assignment chips and the empty-state hint. They are real buttons (focusable,
    /// Space/Enter, a toggle pattern for screen readers) rather than clickable Borders.
    /// </summary>
    internal static class CategoryChips
    {
        /// <summary>Flips <paramref name="name"/> in the assignment, keeping assignment order.</summary>
        internal static void Toggle(HashSet<string> selected, List<string> order, string name)
        {
            if (selected.Remove(name))
                order.RemoveAll(n => string.Equals(n, name, System.StringComparison.OrdinalIgnoreCase));
            else
            {
                selected.Add(name);
                order.Add(name);
            }
        }

        internal static ToggleButton Create(string name, System.Action onClick)
        {
            var chip = new ToggleButton
            {
                Content          = name,
                Tag              = name,
                FontSize         = 11,
                Padding          = new Thickness(7, 2, 7, 2),
                Margin           = new Thickness(0, 0, 4, 4),
                BorderThickness  = new Thickness(1),
                Cursor           = Cursors.Hand,
                Template         = ChipTemplate(typeof(ToggleButton), 3),
                FocusVisualStyle = null
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, name);
            chip.Click += (_, _) => onClick();
            return chip;
        }

        internal static Button CreateHint(System.Action open)
        {
            var text = new TextBlock { FontSize = 9.5, TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(TextBlock.TextProperty, "Str_Hint_NoCategories");
            var hint = new Button
            {
                Content          = text,
                Margin           = new Thickness(0, 0, 0, 10),
                Padding          = new Thickness(0),
                BorderThickness  = new Thickness(0),
                Background       = Brushes.Transparent,
                Cursor           = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Template         = ChipTemplate(typeof(Button), 0),
                FocusVisualStyle = null
            };
            hint.SetResourceReference(Control.ForegroundProperty, "DimTextBrush");
            hint.SetResourceReference(Control.FontFamilyProperty, "UiFont");
            hint.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "Str_Hint_NoCategories");
            hint.Click += (_, _) => open();
            hint.MouseRightButtonUp += (_, e) => { e.Handled = true; open(); };
            return hint;
        }

        // Selected reads as the category filled solid; unselected as its outline only, so the
        // color is visible either way and the state is unambiguous at a glance.
        internal static void Paint(ToggleButton chip, string name, bool on, IList<string> order)
        {
            var color = CategoryManager.ColorOf(name);
            var fill  = CategoryManager.BrushOf(name);

            bool primary = on && order.Count > 0 &&
                string.Equals(order[0], name, System.StringComparison.OrdinalIgnoreCase);
            chip.IsChecked = on;
            chip.BorderBrush = primary ? CategoryManager.ForegroundFor(color) : fill;
            chip.Background = on ? fill : Brushes.Transparent;
            if (on) chip.Foreground = CategoryManager.ForegroundFor(color);
            else chip.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            chip.FontWeight = primary ? FontWeights.Bold : FontWeights.Normal;
            if (on)
            {
                chip.BorderThickness = new Thickness(primary ? 2 : 1);
                chip.Padding = new Thickness(primary ? 6 : 7, primary ? 1 : 2,
                                             primary ? 6 : 7, primary ? 1 : 2);
            }
            else
            {
                chip.BorderThickness = new Thickness(1);
                chip.Padding = new Thickness(7, 2, 7, 2);
            }
        }

        // One bare template: the colors are user data painted onto Background/BorderBrush by
        // Paint, so no theme trigger may override them. Hover dims, keyboard focus draws a ring.
        private static ControlTemplate ChipTemplate(System.Type target, double radius)
        {
            var ring = new FrameworkElementFactory(typeof(Border), "ring");
            ring.SetValue(UIElement.IsHitTestVisibleProperty, false);
            ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius + 1));
            ring.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            ring.SetValue(FrameworkElement.MarginProperty, new Thickness(-2));
            ring.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            ring.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

            var border = new FrameworkElementFactory(typeof(Border), "border");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(ring);
            grid.AppendChild(border);

            var template = new ControlTemplate(target) { VisualTree = grid };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "ring"));
            template.Triggers.Add(focus);
            return template;
        }
    }
}
