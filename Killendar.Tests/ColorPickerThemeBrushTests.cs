using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Decay, Delirium, Ectoplasm, Malaise and Sepulchre paint their surfaces with
    /// gradients, and Blood, Cyanotic and Greed their panes. The picker read every theme brush as a
    /// SolidColorBrush, so opening it (or arming Replace) under those themes threw.</summary>
    public sealed class ColorPickerThemeBrushTests
    {
        private static readonly string[] Keys =
        {
            "SurfaceBrush", "AppBorderBrush", "InputBorderBrush", "PrimaryBrush", "BackgroundBrush",
            "MutedTextBrush", "RowSelectedBrush", "PaneBrush", "TextBrush",
        };

        [Fact]
        public void ThePickerOpensAndArmsReplaceUnderGradientThemeBrushes() => PickerUi.Run(() =>
        {
            var resources = Application.Current.Resources;
            var saved = new Dictionary<string, object?>();
            foreach (var key in Keys)
            {
                saved[key] = resources.Contains(key) ? resources[key] : null;
                resources[key] = new LinearGradientBrush(Colors.Black, Colors.DarkRed, 90);
            }
            try
            {
                var dialog = new ColorPickerDialog(null, Colors.SteelBlue);
                var replace = Find(dialog, "Replace");
                for (int i = 0; i < 2; i++)
                    replace.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                dialog.Close();
            }
            finally
            {
                foreach (var pair in saved)
                    if (pair.Value == null) resources.Remove(pair.Key); else resources[pair.Key] = pair.Value;
            }
        });

        private static FrameworkElement Find(DependencyObject root, string name)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                if (d is FrameworkElement fe && AutomationProperties.GetName(fe) == name) return fe;
                try { return Find(d, name); } catch (KeyNotFoundException) { }
            }
            throw new KeyNotFoundException(name);
        }
    }
}
