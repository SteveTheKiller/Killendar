namespace Killendar.Features.About
{
    /// <summary>APP-OWNED: Killendar's hooks into the shared About controller.</summary>
    internal sealed partial class AboutController
    {
        // No startup update prompt under --demo, so captures stay clean.
        partial void SkipStartupCheck(ref bool skip) => skip |= App.IsDemo;
    }
}
