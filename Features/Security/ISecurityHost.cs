using System.Windows;

namespace Killendar.Features
{
    /// <summary>
    /// What SecurityController needs from the window hosting it. Expressed as intent ("show this
    /// lock state") rather than as controls, so the controller holds no reference to any Button or
    /// TextBlock and can be driven by a stub in a test. Window is the one exception: modal dialogs
    /// need an owner.
    /// </summary>
    internal interface ISecurityHost
    {
        /// <summary>Owner for modal dialogs, and the window to close when an unlock is cancelled
        /// during startup.</summary>
        Window Window { get; }

        /// <summary>Localized string for a Str_ key.</summary>
        string Loc(string key);

        /// <summary>Writes the status line.</summary>
        void SetStatus(string text);

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
