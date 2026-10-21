using System.Windows;
using System.Windows.Controls;
using Killendar.Controls;
using Killendar.Services;

// The toolbar's Deleted button: a flyout of what was deleted in the last 30 days, newest first.
// Clicking an entry puts it back (EventStore.Deleted.cs), and Ctrl+Z takes a restore away again.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private ContextMenu? _deletedMenu;

        private void DeletedBtn_Click(object sender, RoutedEventArgs e)
        {
            _deletedMenu ??= new ContextMenu();
            _deletedMenu.Items.Clear();
            var items = _store.ListDeleted();
            _deletedMenu.Items.Add(new MenuItem
            {
                Header = Loc(items.Count == 0 ? "Str_Del_Empty" : "Str_Del_Header"),
                IsEnabled = false,
            });
            foreach (var item in items)
            {
                string title = string.IsNullOrWhiteSpace(item.Title) ? Loc("Str_Cal_NoTitle") : item.Title;
                var entry = new MenuItem
                {
                    Header = title + "    " + DateFormatManager.MonthDay(item.Start) + " " + DateFormatManager.Time(item.Start),
                    ToolTip = string.Format(Loc("Str_Del_DeletedOn"),
                        DateFormatManager.Format(item.DeletedUtc.ToLocalTime()) + " " + DateFormatManager.Time(item.DeletedUtc.ToLocalTime())),
                };
                var id = item.Id;
                entry.Click += (_, _) =>
                {
                    bool restored = _store.RestoreDeleted(id);
                    if (_agendaDay != null) BuildDayAgendaRows();
                    StatusText.Text = restored
                        ? string.Format(Loc("Str_Status_Restored"), title)
                        : string.Format(Loc("Str_Status_UndoFailed"), _store.LoadError ?? "");
                };
                _deletedMenu.Items.Add(entry);
            }
            if (DeletedBtn.IsVisible)
            {
                // Straight down from the button, which sits at the left of the toolbar.
                _deletedMenu.PlacementTarget = DeletedBtn;
                _deletedMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                _deletedMenu.HorizontalOffset = 0;
                _deletedMenu.VerticalOffset = 4;
            }
            else FlyoutPlacement.Attach(_deletedMenu, this);   // shed into "...": open from the pane
            _deletedMenu.IsOpen = true;
            Anim.FadeIn(_deletedMenu);
        }
    }
}
