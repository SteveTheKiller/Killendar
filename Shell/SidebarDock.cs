using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

// Which side of the calendar the appointment panel and its rail dock to: the left (the default)
// or the right. ScaleHost's three columns are put back in the other order, so SidebarCol keeps its
// identity and the slide, the resize grip and the window-width ride all keep working unchanged;
// everything that leans toward the panel or away from the window edge mirrors with it.
//
// Alt+Shift+Left or Right docks the panel to that side, the family keys for a movable pane, and
// the arrow of the side it is already on opens or closes it, as B does. The rail's right-click
// menu offers the two sides with those keys beside them.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private const string SidebarSideKey = "SidebarSide";
        private bool _sidebarRight;
        private ColumnDefinition _calendarCol = null!;

        private void InitSidebarSide()
        {
            _sidebarRight = string.Equals(Settings.Get(SidebarSideKey), "Right", StringComparison.OrdinalIgnoreCase);
            _calendarCol = ScaleHost.ColumnDefinitions[2];
            // The card's inset comes from the theme; a right-hand panel mirrors it, so it is
            // worked out again when the theme changes.
            Services.ThemeManager.ThemeChanged += ApplyCalendarInset;
            SidebarSplitter.Loaded += (_, _) => ApplySplitterLineSide();
            if (_sidebarRight) ApplySidebarSide();
        }

        // The grab handle's line sits on the panel's inner edge, against the rail, so the rail's
        // glyphs have the same space to it as to the calendar card.
        private void ApplySplitterLineSide()
        {
            if (SidebarSplitter.Template?.FindName("line", SidebarSplitter) is FrameworkElement line)
                line.HorizontalAlignment = _sidebarRight ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }

        private void SelectSidebarSide(bool right)
        {
            if (right == _sidebarRight) return;
            _sidebarRight = right;
            Settings.Set(SidebarSideKey, right ? "Right" : "Left");
            ApplySidebarSide();
        }

        private void ApplySidebarSide()
        {
            // Panel | rail | calendar, or calendar | rail | panel. The rail stays in the middle.
            var cols = ScaleHost.ColumnDefinitions;
            cols.Clear();
            foreach (ColumnDefinition col in _sidebarRight
                         ? new[] { _calendarCol, RailCol, SidebarCol }
                         : new[] { SidebarCol, RailCol, _calendarCol })
                cols.Add(col);
            int panel = _sidebarRight ? 2 : 0;
            Grid.SetColumn(AppointmentSidebar, panel);
            Grid.SetColumn(SidebarSplitter, panel);
            Grid.SetColumn(CalendarArea, 2 - panel);

            // The panel's content is pinned to the rail side, so it slides out from under the rail
            // rather than crawling in from the window edge; the grip rides the same inner lip.
            SidebarContent.HorizontalAlignment = _sidebarRight ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            SidebarSplitter.HorizontalAlignment = _sidebarRight ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            ApplySplitterLineSide();
            SidebarToggleBtn.Content = _sidebarOpen ? ChevronClose : ChevronOpen;
            // The toolbar's wider margin lines its right end up with the card's outer inset; that
            // edge is the window edge, which is now on the left.
            ToolbarGrid.Margin = _sidebarRight ? new Thickness(18, 2, 10, 6) : new Thickness(10, 2, 18, 6);
            ApplyCalendarInset();

            // The footer centers its badge over the calendar, so its columns mirror too.
            var footer = FooterCenterGrid.ColumnDefinitions;
            if (footer.Count == 3 && (footer[0].Width.IsStar != _sidebarRight))
            {
                var parts = new[] { footer[2], footer[1], footer[0] };
                footer.Clear();
                foreach (ColumnDefinition col in parts) footer.Add(col);
            }
            Grid.SetColumn(PortableBadgeHost, _sidebarRight ? 0 : 2);

            Controls.FlyoutPlacement.RailOnRight = _sidebarRight;
        }

        // The calendar card butts against the rail and keeps the theme's inset on its window side.
        private void ApplyCalendarInset()
        {
            if (!_sidebarRight)
            {
                ContentPane.SetResourceReference(MarginProperty, "ContentPaneMargin");
                return;
            }
            Thickness inset = TryFindResource("ContentPaneMargin") is Thickness t ? t : new Thickness(0, 0, 8, 0);
            ContentPane.Margin = new Thickness(inset.Right, inset.Top, inset.Left, inset.Bottom);
        }

        // The rail's (and the panel's) right-click menu: which side the panel docks to.
        private void SidebarArea_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Fields, buttons and lists in the panel keep their own menus.
            for (var node = e.OriginalSource as DependencyObject; node != null && !ReferenceEquals(node, sender);
                 node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node is TextBox || node is ComboBox || node is ButtonBase || node is ListBox)
                    return;

            var menu = new ContextMenu { Style = TryFindResource(typeof(ContextMenu)) as Style };
            menu.Items.Add(new MenuItem { Header = Loc("Str_Sidebar"), IsEnabled = false });
            foreach (bool right in new[] { false, true })
            {
                var item = new MenuItem
                {
                    Header = Loc(right ? "Str_Sidebar_Right" : "Str_Sidebar_Left"),
                    IsCheckable = true,
                    IsChecked = right == _sidebarRight,
                    InputGestureText = right ? "Alt+Shift+Right" : "Alt+Shift+Left",
                    Icon = PaneSideIcon(right ? Dock.Right : Dock.Left),
                };
                item.Click += (_, _) => SelectSidebarSide(right);
                menu.Items.Add(item);
            }
            menu.PlacementTarget = (UIElement)sender;
            menu.IsOpen = true;
            e.Handled = true;
        }

        /// <summary>
        /// A small window with its sidebar shaded on the given side, the icon for a side menu's
        /// items. Drawn in the item's text color, read the way a glyph icon reads it, so it
        /// follows the highlight; a checked item shows its checkmark in this slot instead.
        /// </summary>
        private static FrameworkElement PaneSideIcon(Dock side)
        {
            var frame = new Border { BorderThickness = new Thickness(1) };
            frame.SetBinding(Border.BorderBrushProperty, InheritedForeground());
            bool across = side == Dock.Top || side == Dock.Bottom;
            var bar = new Border
            {
                Width = across ? double.NaN : 3,
                Height = across ? 3 : double.NaN,
                HorizontalAlignment = side == Dock.Right ? HorizontalAlignment.Right
                                    : side == Dock.Left ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
                VerticalAlignment = side == Dock.Bottom ? VerticalAlignment.Bottom
                                  : side == Dock.Top ? VerticalAlignment.Top : VerticalAlignment.Stretch,
            };
            bar.SetBinding(Border.BackgroundProperty, InheritedForeground());
            var icon = new Grid { Width = 12, Height = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            icon.Children.Add(frame);
            icon.Children.Add(bar);
            return icon;
        }

        private static System.Windows.Data.Binding InheritedForeground() => new System.Windows.Data.Binding
        {
            Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty),
            RelativeSource = System.Windows.Data.RelativeSource.Self,
        };
    }
}
