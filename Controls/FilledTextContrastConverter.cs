using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Killendar.Services;

namespace KillerFamily.Controls
{
    /// <summary>Preserves readable theme text, adjusting it only when a filled caption needs more contrast.</summary>
    public sealed class FilledTextContrastConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not SolidColorBrush text)
                return values.Length > 0 ? values[0] : DependencyProperty.UnsetValue;
            Color background;
            if (values[1] is SolidColorBrush solid) background = solid.Color;
            else if (values[1] is GradientBrush gradient && gradient.GradientStops.Count > 0)
            {
                background = Color.FromRgb((byte)gradient.GradientStops.Average(s => s.Color.R),
                    (byte)gradient.GradientStops.Average(s => s.Color.G),
                    (byte)gradient.GradientStops.Average(s => s.Color.B));
            }
            else return text;
            var color = ColorContrast.ReadableOn(text.Color, background, 4.5);
            if (color == text.Color) return text;
            var result = new SolidColorBrush(color);
            result.Freeze();
            return result;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
