using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KillerFamily.Accessibility;
using KillerFamily.Layout;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GM-010, GM-011 and the other small kit fixes that share a seam with them.</summary>
    [Collection("Calendar settings")]
    public sealed class KitSmallFixTests
    {
        private static int Handlers(UIElement element, RoutedEvent routed)
        {
            var store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(element);
            if (store == null) return 0;
            var infos = (RoutedEventHandlerInfo[]?)store.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(store, new object[] { routed });
            return infos?.Length ?? 0;
        }

        [Fact]
        public void TurningWrapContentOnAgainAddsNoSecondSetOfHandlers() => KitTestHost.Sta(() =>
        {
            var tip = new ToolTip { Content = new TextBlock { Text = "Save (Ctrl+S)" } };
            TooltipTextConverter.SetWrapContent(tip, true);
            int opened = Handlers(tip, ToolTip.OpenedEvent), closed = Handlers(tip, ToolTip.ClosedEvent);
            Assert.True(opened >= 1);

            TooltipTextConverter.SetWrapContent(tip, false);
            TooltipTextConverter.SetWrapContent(tip, true);
            TooltipTextConverter.SetWrapContent(tip, false);
            TooltipTextConverter.SetWrapContent(tip, true);

            Assert.Equal(opened, Handlers(tip, ToolTip.OpenedEvent));
            Assert.Equal(closed, Handlers(tip, ToolTip.ClosedEvent));
        });

        [Fact]
        public void ACanceledCloseAfterTheFadeBringsTheWindowBack() => KitTestHost.Sta(() =>
        {
            var window = new Window { Opacity = 0 };
            window.Closing += (_, e) => e.Cancel = true;
            KitTestHost.CallStatic(KitTestHost.AppType("Killendar.Controls.Anim"), "FinishFadeClose", window);
            Assert.Equal(1.0, window.Opacity);
        });

        [Fact]
        public void ACloseThatGoesAheadLeavesTheFadeAlone() => KitTestHost.Sta(() =>
        {
            var window = new Window { Opacity = 0 };
            KitTestHost.CallStatic(KitTestHost.AppType("Killendar.Controls.Anim"), "FinishFadeClose", window);
            Assert.Equal(0.0, window.Opacity);
        });

        [Fact]
        public void ALateSlideCompletionKeepsTheNewerTransform() => KitTestHost.Sta(() =>
        {
            var anim = KitTestHost.AppType("Killendar.Controls.Anim");
            var element = new Border();
            var first = new System.Windows.Media.TranslateTransform(5, 0);
            var second = new System.Windows.Media.TranslateTransform(9, 0);
            element.RenderTransform = second;
            KitTestHost.CallStatic(anim, "EndSlide", element, first);
            Assert.Same(second, element.RenderTransform);
            KitTestHost.CallStatic(anim, "EndSlide", element, second);
            Assert.Null(element.RenderTransform);
            Assert.Equal(System.Windows.Media.Transform.Identity.Value, (element.RenderTransform ?? System.Windows.Media.Transform.Identity).Value);
        });

        [Fact]
        public void ABevelFollowsAHighContrastOrDpiRefresh() => KitTestHost.Sta(() =>
        {
            var text = new TextBlock { Text = "Heading" };
            KillerFamily.Theming.Bevel.SetEnabled(text, true);
            text.Effect = null;   // what a stale bevel looks like after the system setting changed
            KitTestHost.CallStatic(typeof(KillerFamily.Theming.Bevel), "RefreshAll");
            if (!SystemParameters.HighContrast)
                Assert.NotNull(text.Effect);
        });

        [Fact]
        public void AnExpanderOrTabIsNamedFromItsHeaderNotItsBody() => KitTestHost.Sta(() =>
        {
            foreach (HeaderedContentControl control in new HeaderedContentControl[]
            {
                new Expander { Header = "Advanced", Content = "Lots of body text here" },
                new TabItem { Header = "Settings", Content = "Lots of body text here" },
                new GroupBox { Header = "Options", Content = "Lots of body text here" },
            })
            {
                control.ToolTip = new ToolTip { Content = new TextBlock { Text = "Rich" } };
                KitTestHost.CallStatic(typeof(AccessibleControls), "Prepare", control);
                string name = AutomationProperties.GetName(control);
                Assert.True(name == (string)control.Header, control.GetType().Name + " was named \"" + name + "\".");
            }
        });

        [Fact]
        public void AWindowThatStartedMaximizedStillSaves() => KitTestHost.Sta(() =>
        {
            // Its RestoreBounds is empty until it has been normal once; the placement falls back to
            // the window's own left, top and size and still records the maximized state.
            var w = new Window { Left = 100, Top = 120, Width = 500, Height = 400, WindowState = WindowState.Maximized };
            Assert.True(w.RestoreBounds.IsEmpty);
            string? saved = null;
            KitTestHost.CallStatic(KitTestHost.AppType("Killendar.Controls.WindowPlacement"), "Save", w,
                (Action<string, string>)((_, value) => saved = value));
            Assert.Equal("100,120,500,400,1", saved);
        });

        [Fact]
        public void AWindowInTheDeadAreaBetweenMonitorsIsNotOnScreen()
        {
            var placement = KitTestHost.AppType("Killendar.Controls.WindowPlacement");
            var screens = new[] { new Rect(0, 0, 1000, 1000), new Rect(1000, 0, 1000, 500) };
            // Inside the bounding box of both monitors, but below the shorter one.
            Assert.False((bool)KitTestHost.CallStatic(placement, "IsReachable", 1100.0, 600.0, 400.0, screens)!);
            Assert.True((bool)KitTestHost.CallStatic(placement, "IsReachable", 1100.0, 200.0, 400.0, screens)!);
            Assert.True((bool)KitTestHost.CallStatic(placement, "IsReachable", 900.0, 300.0, 400.0, screens)!);
            Assert.False((bool)KitTestHost.CallStatic(placement, "IsReachable", 2500.0, 100.0, 400.0, screens)!);
        }

        [Fact]
        public void ASavedSizeLargerThanTheDesktopIsClamped()
        {
            var placement = KitTestHost.AppType("Killendar.Controls.WindowPlacement");
            var size = ((double, double))KitTestHost.CallStatic(placement, "ClampSize", 9000.0, 7000.0, 1920.0, 1080.0, 400.0, 300.0)!;
            Assert.Equal((1920.0, 1080.0), size);
            size = ((double, double))KitTestHost.CallStatic(placement, "ClampSize", 100.0, 100.0, 1920.0, 1080.0, 400.0, 300.0)!;
            Assert.Equal((400.0, 300.0), size);
        }

        [Fact]
        public void TreeElbowsFollowAnInsertAfterTheLastChild() => KitTestHost.Sta(() =>
        {
            var tree = new TreeView();
            var a = new TreeViewItem { Header = "a" };
            tree.Items.Add(a);
            tree.Measure(new Size(200, 200));
            tree.Arrange(new Rect(0, 0, 200, 200));
            tree.UpdateLayout();

            // The same two inputs the template's trigger binds: the container and its parent's count.
            var probe = new Border();
            var multi = new System.Windows.Data.MultiBinding { Converter = new Killendar.Controls.LastChildConverter() };
            multi.Bindings.Add(new System.Windows.Data.Binding { Source = a });
            multi.Bindings.Add(new System.Windows.Data.Binding("Items.Count") { Source = tree });
            System.Windows.Data.BindingOperations.SetBinding(probe, FrameworkElement.TagProperty, multi);
            Assert.Equal(true, probe.Tag);

            tree.Items.Add(new TreeViewItem { Header = "b" });
            tree.UpdateLayout();

            Assert.Equal(false, probe.Tag);
        });

        [Fact]
        public void TheTreeTemplateBindsTheElbowToTheParentCountToo()
        {
            string xaml = KitTestHost.Source(@"Controls\Controls.xaml");
            Assert.DoesNotContain("{Binding RelativeSource={RelativeSource Self}, Converter={StaticResource IsLastChild}}", xaml);
        }

        [Fact]
        public void ABadAuditFolderDoesNotThrowOnTheDispatcher() => KitTestHost.Sta(() =>
        {
            string? previous = Environment.GetEnvironmentVariable("KILLER_ACCESSIBILITY_REPORT");
            string file = Path.Combine(Path.GetTempPath(), "kit-audit-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(file, "not a folder");
            try
            {
                // A folder path that is really a file: CreateDirectory throws IOException.
                Environment.SetEnvironmentVariable("KILLER_ACCESSIBILITY_REPORT", file);
                var window = new Window();
                var ex = Record.Exception(() => KitTestHost.CallStatic(typeof(AccessibilityAudit), "Record", window));
                Assert.Null(ex);
            }
            finally
            {
                Environment.SetEnvironmentVariable("KILLER_ACCESSIBILITY_REPORT", previous);
                File.Delete(file);
            }
        });
    }
}
