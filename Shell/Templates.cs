using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Killendar.Models;

// Duplicate and templates (S15). Duplicate turns the open appointment into a new one; a template
// is an appointment's details kept in the calendar file under its title (EventStore.Templates.cs).
// Templates are used from the editor's Templates button and from the New button's right-click.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void DuplicateAppointment_Click(object sender, RoutedEventArgs e) => _appointments.Duplicate();

        private void TemplatesBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = TemplatesBtn, Placement = PlacementMode.Top };
            var save = new MenuItem { Header = Loc("Str_Tpl_Save") };
            save.Click += (_, _) => _appointments.SaveTemplate();
            menu.Items.Add(save);
            menu.Items.Add(new Separator());
            AddTemplateItems(menu, EditorDay());
            menu.IsOpen = true;
        }

        /// <summary>The day the editor is on, for a template applied from inside it.</summary>
        private DateTime EditorDay() =>
            Features.AppointmentEditor.TryParseDate(FieldStartDate.Text, out var day) ? day.Date : _calendar.Anchor.Date;

        /// <summary>One entry per template, then Delete template for each.</summary>
        private void AddTemplateItems(ItemsControl menu, DateTime day)
        {
            var templates = _store.ListTemplates();
            if (templates.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = Loc("Str_Tpl_None"), IsEnabled = false });
                return;
            }
            foreach (var template in templates)
            {
                var use = new MenuItem { Header = template.Title };
                use.Click += (_, _) => _appointments.NewFromTemplate(template, day);
                menu.Items.Add(use);
            }
            menu.Items.Add(new Separator());
            var delete = new MenuItem { Header = Loc("Str_Tpl_Delete") };
            foreach (var template in templates)
            {
                var item = new MenuItem { Header = template.Title };
                string name = template.Title;
                item.Click += (_, _) =>
                {
                    _store.DeleteTemplate(name);
                    StatusText.Text = string.Format(Loc("Str_Status_TplDeleted"), name);
                };
                delete.Items.Add(item);
            }
            menu.Items.Add(delete);
        }

        /// <summary>The New button's right-click: start from a template on the day in view.</summary>
        private void InitTemplateMenu()
        {
            NewEventBtn.ContextMenu = new ContextMenu();
            NewEventBtn.ContextMenuOpening += (_, e) =>
            {
                var menu = NewEventBtn.ContextMenu;
                menu.Items.Clear();
                menu.Items.Add(new MenuItem { Header = Loc("Str_Tpl_NewFrom"), IsEnabled = false });
                AddTemplateItems(menu, _calendar.Anchor.Date);
            };
        }
    }
}
