using System.Windows;
using System.Windows.Input;
using Killendar.Features;

// MainWindow's side of the security feature: it satisfies ISecurityHost and forwards the two
// button clicks. All the behaviour lives in Features/Security/SecurityController.cs.
namespace Killendar
{
    public partial class MainWindow : ISecurityHost
    {
        private SecurityController _security = null!;

        /// <summary>Lock (0xE72E) when encrypted, unlock (0xE785) when plaintext, Segoe MDL2.
        /// Written as char casts so the private-use glyphs cannot be mangled by tooling.</summary>
        void ISecurityHost.ShowLockState(bool encrypted)
        {
            LockButton.Content = ((char)(encrypted ? 0xE72E : 0xE785)).ToString();
            LockButton.ToolTip = Loc(encrypted ? "Str_Tip_LockOn" : "Str_Tip_LockOff");
        }

        void ISecurityHost.ShowActiveKillendar(string name, bool visible)
        {
            ActiveKillendarLabel.Text = name;
            ActiveKillendarLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        void ISecurityHost.RefreshView() => _calendar.Refresh();

        void ISecurityHost.CloseSidebar() => CloseSidebar();

        private void KillendarsButton_Click(object sender, RoutedEventArgs e)
            => _security.ShowKillendars();

        private void LockButton_Click(object sender, RoutedEventArgs e)
            => _security.ToggleLock();

        /// <summary>Footer label click - same destination as the title-bar button.</summary>
        private void ActiveKillendarLabel_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _security.ShowKillendars();
        }
    }
}
