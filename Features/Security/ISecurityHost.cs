namespace Killendar.Features
{
    /// <summary>
    /// What SecurityController needs from the window hosting it, beyond the shared shell services.
    /// Expressed as intent ("show this lock state") rather than as controls, so the controller holds
    /// no reference to any Button or TextBlock and can be driven by a stub in a test.
    /// </summary>
    internal interface ISecurityHost : IShellServices
    {
        /// <summary>Reflects whether the open Killendar is encrypted.</summary>
        void ShowLockState(bool encrypted);

        /// <summary>Shows which Killendar is open, or hides the label when there is nothing worth
        /// saying.</summary>
        void ShowActiveKillendar(string name, bool visible);

        /// <summary>Repaints the current calendar view after the store changes underneath it.</summary>
        void RefreshView();

        /// <summary>Closes the appointment sidebar; what it was editing may be about to disappear
        /// with the Killendar being closed.</summary>
        void CloseSidebar();
    }
}
