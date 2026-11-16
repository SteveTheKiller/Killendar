using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Killendar.Controls
{
    /// <summary>Gives every TextBox, RichTextBox and PasswordBox in the app the family
    /// TextInputContextMenu (Controls.xaml) instead of the stock Windows Cut/Copy/Paste menu.
    /// Per-style setters miss boxes on other styles and boxes built in code, so this is one class
    /// handler, registered once at startup. A box that sets its own ContextMenu keeps it.</summary>
    internal static class TextInputMenu
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered) return;
            _registered = true;
            // Loaded, not ContextMenuOpening: by then the stock TextBox handler has already decided
            // to show its own menu.
            EventManager.RegisterClassHandler(typeof(TextBoxBase), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(Assign), handledEventsToo: true);
            EventManager.RegisterClassHandler(typeof(PasswordBox), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(Assign), handledEventsToo: true);
        }

        private static void Assign(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement box || box.ContextMenu != null) return;
            box.SetResourceReference(FrameworkElement.ContextMenuProperty, "TextInputContextMenu");
        }
    }
}
