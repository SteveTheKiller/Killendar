using System;
using System.Windows;
using System.Windows.Controls;
using Killendar.Features;
using Killendar.Services;

// The editor's time zone choice, shown once "Show time zones" is ticked in the date settings.
// The first entry is this computer's own zone; the rest are every zone Windows knows, by its
// display name. The list is built when the panel asks for it, so a changed language or a new
// Windows zone list shows the next time an appointment opens.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private string _zoneId = "";
        private bool _buildingZone;

        string IAppointmentView.FieldZoneId
        {
            get => _zoneId;
            set { _zoneId = value ?? ""; if (ZoneRow.Visibility == Visibility.Visible) BuildZoneChoices(); }
        }

        void IAppointmentView.ShowZone(bool visible, bool editable)
        {
            ZoneRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            FieldZone.IsEnabled = editable;
            FieldZone.ToolTip = editable ? null : Loc("Str_TT_ZoneOfSeries");
            if (visible) BuildZoneChoices();
        }

        private void InitZoneField()
        {
            FieldZone.SelectionChanged += (_, _) =>
            {
                if (_buildingZone || FieldZone.SelectedItem is not ComboBoxItem item) return;
                _zoneId = item.Tag as string ?? "";
                _appointments.SyncHighlightedSelection();
            };
        }

        private void BuildZoneChoices()
        {
            _buildingZone = true;
            try
            {
                FieldZone.Items.Clear();
                var own = new ComboBoxItem
                {
                    Content = string.Format(Loc("Str_Zone_ThisComputer"), TimeZoneInfo.Local.DisplayName),
                    Tag = "",
                };
                FieldZone.Items.Add(own);
                FieldZone.SelectedItem = own;
                var chosen = TimeZones.Find(_zoneId);
                foreach (var zone in TimeZones.Choices(_zoneId))
                {
                    var item = new ComboBoxItem { Content = zone.DisplayName, Tag = zone.Id };
                    FieldZone.Items.Add(item);
                    if (chosen != null && zone.Id == chosen.Id) FieldZone.SelectedItem = item;
                }
                // An imported IANA name ("America/New_York") is kept as written; the list shows
                // its Windows match.
                if (chosen == null) _zoneId = "";
            }
            finally { _buildingZone = false; }
        }
    }
}
