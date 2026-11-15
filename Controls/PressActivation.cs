using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace Killendar.Controls
{
    /// <summary>
    /// Makes a plain element act like a button: it activates on a full click (press, then release
    /// on the same element) and on Enter or Space, and it can take keyboard focus. Activating on
    /// the release alone would fire for a drag that merely ended over the element.
    /// </summary>
    internal static class PressActivation
    {
        private static readonly DependencyProperty ActivateProperty = DependencyProperty.RegisterAttached(
            "Activate", typeof(Action), typeof(PressActivation));

        internal static void Attach(UIElement element, Action activate)
        {
            bool pressed = false;
            element.SetValue(ActivateProperty, activate);
            element.Focusable = true;
            KeyboardNavigation.SetIsTabStop(element, true);
            element.MouseLeftButtonDown += (_, _) => pressed = true;
            element.MouseLeave += (_, _) => pressed = false;
            element.MouseLeftButtonUp += (_, _) =>
            {
                if (!pressed) return;
                pressed = false;
                activate();
            };
            element.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                activate();
            };
        }

        internal static Action? ActivationOf(UIElement element) => element.GetValue(ActivateProperty) as Action;
    }

    /// <summary>A StackPanel that a screen reader hears as a button and can invoke, for rows
    /// that <see cref="PressActivation"/> turns into buttons.</summary>
    internal sealed class PressablePanel : StackPanel
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

        private sealed class Peer : FrameworkElementAutomationPeer, IInvokeProvider
        {
            public Peer(PressablePanel owner) : base(owner) { }

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
            protected override string GetClassNameCore() => nameof(PressablePanel);
            protected override bool IsControlElementCore() => true;

            public override object GetPattern(PatternInterface patternInterface) =>
                patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

            public void Invoke()
            {
                if (!IsEnabled()) throw new System.Windows.Automation.ElementNotEnabledException();
                PressActivation.ActivationOf(Owner)?.Invoke();
            }
        }
    }
}
