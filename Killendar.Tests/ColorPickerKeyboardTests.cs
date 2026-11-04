using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The color picker and the new-category swatch work from the keyboard: every swatch,
    /// the Replace and Reset chips, the saturation and brightness square and the hue strip are
    /// focusable and named, Enter or Space invokes, and the arrows move the square and the strip.</summary>
    public sealed class ColorPickerKeyboardTests
    {
        [Theory]
        [InlineData(Key.Enter)]
        [InlineData(Key.Space)]
        public void EnterAndSpaceInvokeAPickerControl(Key key) => Assert.True(ColorPickerKeys.Activates(key));

        [Fact]
        public void OtherKeysDoNotInvoke() => Assert.False(ColorPickerKeys.Activates(Key.Tab));

        [Theory]
        [InlineData(Key.Right, false, 0.51, 0.50)]
        [InlineData(Key.Left, false, 0.49, 0.50)]
        [InlineData(Key.Up, false, 0.50, 0.51)]
        [InlineData(Key.Down, false, 0.50, 0.49)]
        [InlineData(Key.Right, true, 0.60, 0.50)]
        public void ArrowsMoveTheSquare(Key key, bool coarse, double s, double v)
        {
            var moved = ColorPickerKeys.NudgeSv(key, coarse, 0.5, 0.5);
            Assert.NotNull(moved);
            Assert.Equal(s, moved!.Value.S, 6);
            Assert.Equal(v, moved.Value.V, 6);
        }

        [Fact]
        public void TheSquareStopsAtItsEdges()
        {
            var moved = ColorPickerKeys.NudgeSv(Key.Right, true, 0.95, 0.0);
            Assert.Equal(1.0, moved!.Value.S, 6);
            Assert.Equal(0.0, ColorPickerKeys.NudgeSv(Key.Down, false, 0.3, 0.0)!.Value.V, 6);
        }

        [Fact]
        public void TabIsNotASquareKey() => Assert.Null(ColorPickerKeys.NudgeSv(Key.Tab, false, 0.5, 0.5));

        [Theory]
        [InlineData(Key.Down, false, 121)]
        [InlineData(Key.Up, false, 119)]
        [InlineData(Key.Down, true, 130)]
        public void ArrowsMoveTheHueStrip(Key key, bool coarse, double hue)
            => Assert.Equal(hue, ColorPickerKeys.NudgeHue(key, coarse, 120)!.Value, 6);

        [Fact]
        public void TheHueStripStopsAtItsEnds()
        {
            Assert.Equal(0, ColorPickerKeys.NudgeHue(Key.Up, true, 4)!.Value, 6);
            Assert.Equal(360, ColorPickerKeys.NudgeHue(Key.Down, true, 355)!.Value, 6);
        }

        [Fact]
        public void EveryPickerControlIsAFocusableNamedStop() => PickerUi.Run(() =>
        {
            var dialog = new ColorPickerDialog(null, Colors.SteelBlue);
            var stops = Descendants(dialog).OfType<Border>().Where(b => b.Focusable).ToList();
            var names = stops.Select(AutomationProperties.GetName).ToList();
            Assert.Contains("Saturation and brightness", names);
            Assert.Contains("Hue", names);
            Assert.Contains("Replace", names);
            Assert.Contains("Reset", names);
            Assert.Equal(9, names.Count(n => n.Length == 7 && n[0] == '#'));
            Assert.All(stops, b => Assert.True(KeyboardNavigation.GetIsTabStop(b)));
            dialog.Close();
        });

        [Fact]
        public void TheNewCategorySwatchIsANamedButton()
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var dialog = XDocument.Load(Path.Combine(Root(), "Controls", "CategoriesDialog.xaml"));
            var swatch = dialog.Descendants().SingleOrDefault(e => (string?)e.Attribute(xaml + "Name") == "NewColorSwatch");
            Assert.True(swatch is not null, "CategoriesDialog.xaml has no NewColorSwatch.");
            Assert.Equal(wpf + "Button", swatch!.Name);
            Assert.Equal("NewColorSwatch_Click", (string?)swatch.Attribute("Click"));
            Assert.NotNull(swatch.Attribute("AutomationProperties.Name"));
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                yield return d;
                foreach (var nested in Descendants(d)) yield return nested;
            }
        }

        // From this file's own path, so the check works wherever the test build lands.
        private static string Root([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
            if (!Directory.Exists(Path.Combine(root, "Controls")))
                throw new InvalidOperationException("Controls/ was not found beside the test project.");
            return root;
        }
    }

    /// <summary>One STA thread with its own dispatcher and the one Application, shared by the
    /// color picker tests. An Application belongs to the thread that made it, so a second test
    /// thread must not build its own resources on it.</summary>
    internal static class PickerUi
    {
        private static readonly Lazy<System.Windows.Threading.Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

        internal static void Run(Action body)
        {
            Exception? failure = null;
            var operation = Ui.Value.BeginInvoke(new Action(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            }));
            Assert.True(operation.Wait(TimeSpan.FromSeconds(20)) == System.Windows.Threading.DispatcherOperationStatus.Completed,
                "The color picker did not build.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private static System.Windows.Threading.Dispatcher Start()
        {
            System.Windows.Threading.Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                // The picker reads its brushes and words from the application, as in the app.
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Controls/AppStyles.xaml", "Strings/en-US.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                    });
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(ready.Wait(TimeSpan.FromSeconds(20)), "The color picker thread did not start.");
            return dispatcher!;
        }
    }
}
