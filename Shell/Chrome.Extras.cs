using System;
using System.Windows;
using System.Windows.Interop;
using Killendar.Controls;

namespace Killendar.Shell
{
    /// <summary>
    /// APP-OWNED: Killendar's side of the family window chrome (Shell/Chrome.cs): the close path
    /// (unsaved input, close to tray, the exit fade), restoring to maximized after a minimize,
    /// square corners on 98SE, and the themed system menu hook.
    /// </summary>
    public partial class MainWindow : Features.About.IAboutDraftGuard
    {
        // The first close request is held while the whole window fades. The helper sets this
        // before issuing its own deferred Close(), so Alt+F4, the caption button, the system
        // menu and Application.Shutdown all share the same exit animation without recursion.
        private bool _closeFaded;
        private WindowState _stateBeforeMinimize = WindowState.Normal;
        private bool _restoringFromMinimize;

        /// <summary>Call from the constructor right after SourceInitialized is wired to
        /// MainWindow_SourceInitialized.</summary>
        private void InitChromeExtras()
        {
            SourceInitialized += (_, _) =>
            {
                if (WindowState != WindowState.Minimized) _stateBeforeMinimize = WindowState;
                // Swallow the two messages that raise Windows' stock white system menu and show
                // the themed one instead (SystemMenu.cs).
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(SystemMenuHook);
            };
        }

        private IntPtr SystemMenuHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (TryHandleSystemMenu(msg, wParam, lParam)) handled = true;
            return IntPtr.Zero;
        }

        // The flat 98SE caption keeps square corners even when floating.
        partial void AdjustRoundedCorners(ref bool rounded)
        {
            if (Services.ThemeManager.Current == Services.Theme.SE98) rounded = false;
        }

        // A window minimized from maximized comes back maximized.
        partial void AfterWindowStateChanged()
        {
            if (WindowState == WindowState.Minimized)
            {
                _restoringFromMinimize = true;
                return;
            }
            if (_restoringFromMinimize && WindowState == WindowState.Normal &&
                _stateBeforeMinimize == WindowState.Maximized)
            {
                _restoringFromMinimize = false;
                WindowState = WindowState.Maximized;
                return;
            }
            _restoringFromMinimize = false;
            _stateBeforeMinimize = WindowState;
        }

        // The same WPF shutdown flag CloseToTray reads; that family file stays a verbatim copy.
        private static readonly System.Reflection.PropertyInfo? s_isShuttingDown = typeof(Application)
            .GetProperty("IsShuttingDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        private static bool ShutdownStarted() => s_isShuttingDown?.GetValue(null) is true;

        /// <summary>Which prompt guards an unsaved appointment draft on the way out.</summary>
        internal static ClosePrompt ClosePromptFor(bool isLocked, bool shutdownStarted) =>
            shutdownStarted ? ClosePrompt.None
            : isLocked ? ClosePrompt.DiscardDraft
            : ClosePrompt.ReplaceDraft;

        /// <summary>Asks the right question for <see cref="ClosePromptFor"/>. False means the user
        /// chose to keep editing.</summary>
        internal static bool ConfirmClose(bool isLocked, bool shutdownStarted, Func<bool> replace, Func<bool> discard) =>
            ClosePromptFor(isLocked, shutdownStarted) switch
            {
                ClosePrompt.None => true,
                ClosePrompt.DiscardDraft => discard(),
                _ => replace(),
            };

        /// <summary>The draft question for an app-initiated exit (Update now, the Install badge),
        /// asked before the shutdown starts because OnClosing stays silent once it has.</summary>
        internal bool ConfirmDraftBeforeExit() =>
            ConfirmClose(_security.IsLocked, false, _appointments.ConfirmReplace,
                         ((Features.ISecurityHost)this).ConfirmDiscardDraft);

        bool Features.About.IAboutDraftGuard.ConfirmDraftBeforeExit() => ConfirmDraftBeforeExit();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Unsaved appointment input is asked about BEFORE the tray can hide the window. Once
            // hidden, a shutdown (sign-out, an update) is a way out that ignores a cancel and so
            // cannot ask; asking here means a hidden window never holds unsaved input. The
            // fade's second pass through here finds nothing left to ask.
            if (!_closeFaded && !ConfirmClose(_security.IsLocked, ShutdownStarted(),
                                              _appointments.ConfirmReplace,
                                              ((Features.ISecurityHost)this).ConfirmDiscardDraft))
            {
                e.Cancel = true;
                return;
            }
            // Close to system tray: hide before the fade, so the window comes back as it was.
            if (_tray.HideInstead(e)) return;
            if (Anim.FadeOutAndClose(this, ref _closeFaded))
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }
    }
}

namespace Killendar.Shell
{
    public enum ClosePrompt { None, ReplaceDraft, DiscardDraft }
}
