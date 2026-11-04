using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The picker's R, G, B and Hex boxes show only short labels, so each needs its own
    /// spoken name for screen readers.</summary>
    public sealed class ColorPickerFieldNameTests
    {
        [Fact]
        public void PrecisionColorFieldsHaveSpokenNames() => PickerUi.Run(() =>
        {
            var dialog = new Killendar.Controls.ColorPickerDialog(null, Colors.SteelBlue);
            var boxes = Descendants(dialog).OfType<TextBox>().ToList();
            var names = boxes.Select(AutomationProperties.GetName).ToList();
            foreach (string expected in new[] { "Red", "Green", "Blue", "Hex color" })
                Assert.Contains(expected, names);
            dialog.Close();
        });

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                yield return d;
                foreach (var nested in Descendants(d)) yield return nested;
            }
        }
    }
}
