namespace Killendar.Features.About
{
    /// <summary>What Update now asks the window before the app shuts down: a pending appointment
    /// draft is never dropped without a prompt, and the window's normal close prompt cannot run
    /// once the shutdown has started.</summary>
    internal interface IAboutDraftGuard
    {
        /// <summary>True when there is nothing to lose or the user chose to discard it.</summary>
        bool ConfirmDraftBeforeExit();
    }

    /// <summary>APP-OWNED: Killendar's hooks into the shared About controller.</summary>
    internal sealed partial class AboutController
    {
        // No startup update prompt under --demo, so captures stay clean.
        partial void SkipStartupCheck(ref bool skip) => skip |= App.IsDemo;

        // The update ends in Application.Shutdown(), after which OnClosing no longer asks.
        partial void ConfirmUpdateShutdown(ref bool proceed) => proceed &= AllowsShutdown(_host);

        /// <summary>False when the host holds a draft the user chose to keep editing.</summary>
        internal static bool AllowsShutdown(object host) =>
            host is not IAboutDraftGuard guard || guard.ConfirmDraftBeforeExit();
    }
}
