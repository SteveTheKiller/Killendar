using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Killendar.Features;
using Killendar.Services;

// The editor's reminder choice. Built in code because the labels depend on all-day: an all-day
// appointment is reminded the day before at 9:00, so it offers just that or nothing. A lead
// imported from another calendar that is not one of the choices is listed as it is.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private int? _reminder;
        private bool _buildingReminder;

        int? IAppointmentView.FieldReminder
        {
            get => _reminder;
            set { _reminder = value; BuildReminderChoices(); }
        }

        private void InitReminderField()
        {
            AllDayToggle.Checked += (_, _) => BuildReminderChoices();
            AllDayToggle.Unchecked += (_, _) => BuildReminderChoices();
            FieldReminder.SelectionChanged += (_, _) =>
            {
                if (!_buildingReminder && FieldReminder.SelectedItem is ComboBoxItem item)
                    _reminder = item.Tag as int?;
            };
            BuildReminderChoices();
        }

        private void BuildReminderChoices()
        {
            _buildingReminder = true;
            try
            {
                bool allDay = AllDayToggle.IsChecked == true;
                var choices = new List<int?> { null };
                if (allDay) choices.Add(_reminder ?? 1440);
                else
                {
                    choices.AddRange(Reminders.Choices.Select(m => (int?)m));
                    if (_reminder is int lead && !choices.Contains(lead))
                        choices = [null, .. choices.Skip(1).Append(lead).OrderBy(m => m)];
                }
                FieldReminder.Items.Clear();
                foreach (var minutes in choices)
                {
                    var item = new ComboBoxItem { Content = Reminders.Label(minutes, allDay, Loc), Tag = minutes };
                    FieldReminder.Items.Add(item);
                    if (minutes == _reminder || (allDay && minutes.HasValue && _reminder.HasValue)) FieldReminder.SelectedItem = item;
                }
            }
            finally { _buildingReminder = false; }
        }
    }
}
