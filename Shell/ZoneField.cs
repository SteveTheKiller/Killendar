using System;
using System.Linq;
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
            set { _zoneId = value ?? ""; if (ZoneRow.Visibility == Visibility.Visible) SelectZone(); }
        }

        void IAppointmentView.ShowZone(bool visible, bool editable)
        {
            ZoneRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            FieldZone.IsEnabled = editable;
            FieldZone.ToolTip = editable ? null : Loc("Str_TT_ZoneOfSeries");
            if (visible) SelectZone();
        }

        /// <summary>Lights the current zone, building the list the first time and whenever a zone
        /// is not in it (one from an imported file that Windows lists under another id).</summary>
        private void SelectZone()
        {
            var chosen = TimeZones.Find(_zoneId);
            if (FieldZone.Items.Count == 0 ||
                (chosen != null && !FieldZone.Items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == chosen.Id)))
            {
                BuildZoneChoices();
                return;
            }
            _buildingZone = true;
            try
            {
                FieldZone.SelectedItem = FieldZone.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => chosen == null ? (string)i.Tag == "" : (string)i.Tag == chosen.Id);
                if (chosen == null) _zoneId = "";
            }
            finally { _buildingZone = false; }
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
