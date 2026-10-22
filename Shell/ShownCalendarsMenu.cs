using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Killendar.Controls;
using Killendar.Services;

// Right-click on the Killendars button: the other Killendars in the data folder, ticked to show
// them beside the open one in their own color (ShownCalendars). Each row says when a calendar is
// locked or cannot be read, and ticking a locked one asks for its password.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private ContextMenu? _shownMenu;

        private void InitShownCalendars()
        {
            ShownCalendars.Changed += () => _calendar?.Refresh();
            _shownMenu = new ContextMenu();
            KillendarsButton.ContextMenu = _shownMenu;
            KillendarsButton.ContextMenuOpening += (_, e) =>
            {
                e.Handled = true;
                if (_security.IsLocked) return;
                OpenShownMenu();
            };
        }

        private void OpenShownMenu()
        {
            if (_shownMenu == null) return;
            BuildShownMenu();
            FlyoutPlacement.Attach(_shownMenu, KillendarsButton);
            _shownMenu.IsOpen = true;
            Anim.FadeIn(_shownMenu);
        }

        private void BuildShownMenu()
        {
            _shownMenu!.Items.Clear();
            var panel = new StackPanel { Margin = new Thickness(10), MinWidth = 200 };
            panel.Children.Add(new TextBlock
            {
                Text = Loc("Str_Shown_Head"), FontSize = 10, Margin = new Thickness(0, 0, 0, 6)
            });
            var others = EventStore.ListKillendars()
                .Where(n => !string.Equals(n, System.IO.Path.GetFileName(EventStore.ActiveFile), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (others.Count == 0)
            {
                var none = new TextBlock { Text = Loc("Str_Shown_None"), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 };
                none.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
                panel.Children.Add(none);
            }
            var remembered = ShownCalendars.Parse(Settings.Get(ShownCalendars.Setting));
            foreach (var name in others)
            {
                var item = ShownCalendars.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
                bool on = remembered.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
                var tick = new CheckBox
                {
                    Content = ShownCalendars.DisplayName(name), IsChecked = on,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                System.Windows.Automation.AutomationProperties.SetName(tick, ShownCalendars.DisplayName(name));
                row.Children.Add(tick);
                if (on && item != null)
                {
                    var swatch = new Rectangle
                    {
                        Width = 10, Height = 10, Margin = new Thickness(8, 0, 0, 0), RadiusX = 2, RadiusY = 2,
                        Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color)),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    row.Children.Add(swatch);
                    string? state = item.State switch
                    {
                        ShownCalendars.State.Locked => Loc("Str_Shown_Locked"),
                        ShownCalendars.State.Unavailable => Loc("Str_Shown_Unavailable"),
                        _ => null,
                    };
                    if (state != null)
                    {
                        var note = new TextBlock { Text = state, FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                        note.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
                        if (item.Error != null) note.ToolTip = item.Error;
                        row.Children.Add(note);
                    }
                    if (item.State == ShownCalendars.State.Locked)
                    {
                        var unlock = new Button
                        {
                            Content = ((char)0xE785).ToString(), FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            FontSize = 12, Margin = new Thickness(6, 0, 0, 0), ToolTip = Loc("Str_Shown_Unlock"),
                        };
                        unlock.SetResourceReference(StyleProperty, "RailButton");
                        System.Windows.Automation.AutomationProperties.SetName(unlock, Loc("Str_Shown_Unlock"));
                        unlock.Click += (_, _) => UnlockShown(item);
                        row.Children.Add(unlock);
                    }
                }
                tick.Click += (_, _) =>
                {
                    ShownCalendars.Toggle(name, tick.IsChecked == true, EventStore.ActiveFile);
                    var added = ShownCalendars.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (added?.State == ShownCalendars.State.Locked) UnlockShown(added);
                    else BuildShownMenu();
                };
                panel.Children.Add(row);
            }
            var hint = new TextBlock
            {
                Text = Loc("Str_Shown_Hint"), FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxWidth = 240,
                Margin = new Thickness(0, 8, 0, 0),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
            panel.Children.Add(hint);
            _shownMenu.Items.Add(new MenuItem
            {
                Header = panel,
                StaysOpenOnClick = true,
                Style = (Style)Application.Current.FindResource("PanelMenuItem"),
            });
        }

        /// <summary>Asks for a shown calendar's own password. It opens for this session only.</summary>
        private void UnlockShown(ShownCalendars.Item item)
        {
            if (_shownMenu != null) _shownMenu.IsOpen = false;
            var dlg = new PasswordDialog(Loc("Str_Pw_UnlockHead"),
                string.Format(Loc("Str_Pw_Protected"), ShownCalendars.DisplayName(item.Name)),
                Loc("Str_Shown_Unlock")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed || string.IsNullOrEmpty(dlg.Password)) return;
            StatusText.Text = ShownCalendars.Unlock(item, dlg.Password)
                ? string.Format(Loc("Str_Shown_Unlocked"), ShownCalendars.DisplayName(item.Name))
                : Loc("Str_Pw_WrongPw");
        }
    }
}
