using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Input;
using System.Xml.Linq;
using Killendar.Controls;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class DatePickerBoundsRegressionTests
    {
        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(9999, 12, 31)]
        public void BoundaryMonthKeepsEveryRepresentableDayInItsGrid(int year, int month, int day)
        {
            RunSta(() =>
            {
                var target = CreateTarget(new DateTime(year, month, day));
                var menu = DatePickerFlyout.Create(target, target);
                var root = Assert.IsType<StackPanel>(Assert.IsType<MenuItem>(Assert.Single(menu.Items.Cast<object>())).Header);
                var grid = Assert.IsType<Grid>(root.Children[1]);
                var labels = grid.Children.OfType<ContentControl>().Select(cell => Assert.IsType<TextBlock>(cell.Content).Text).ToArray();

                Assert.Contains("1", labels);
                Assert.Contains("31", labels);
                Assert.Equal(210, root.Width);
                Assert.Equal(7, grid.ColumnDefinitions.Count);
                Assert.Equal(7, grid.RowDefinitions.Count);
            });
        }

        [Theory]
        [InlineData(1, 1, 0)]
        [InlineData(9999, 12, 2)]
        public void NavigationCannotLeaveTheSupportedMonthRange(int year, int month, int disabledIndex)
        {
            RunSta(() =>
            {
                var target = CreateTarget(new DateTime(year, month, 1));
                var menu = DatePickerFlyout.Create(target, target);
                var root = (StackPanel)((MenuItem)menu.Items[0]).Header;
                var header = (Grid)root.Children[0];
                var disabled = (UIElement)header.Children[disabledIndex];

                Assert.False(disabled.IsEnabled);
                disabled.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                Assert.Same(root, ((MenuItem)menu.Items[0]).Header);
            });
        }

        internal static TextBox CreateTarget(DateTime date)
        {
            var target = new TextBox { Text = DateFormatManager.Format(date) };
            XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Controls", "Controls.xaml")))
                root = Path.GetDirectoryName(root);
            if (root == null) throw new DirectoryNotFoundException("Controls.xaml not found above test output.");
            var source = XDocument.Load(Path.Combine(root, "Controls", "Controls.xaml"));
            var style = new XElement(source.Descendants(w + "Style").Single(element => (string?)element.Attribute(x + "Key") == "PanelMenuItem"));
            style.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
            target.Resources["PanelMenuItem"] = (Style)XamlReader.Parse(style.ToString());
            var strings = XDocument.Load(Path.Combine(root, "Strings", "en-US.xaml"));
            foreach (string key in new[] { "Str_TT_Prev", "Str_TT_Next" })
                target.Resources[key] = strings.Descendants().Single(element => (string?)element.Attribute(x + "Key") == key).Value;
            return target;
        }

        internal static void RunSta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    CultureInfo.CurrentCulture = new CultureInfo("en-US");
                    action();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
