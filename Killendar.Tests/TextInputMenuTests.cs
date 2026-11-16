using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Every text box gets the family Cut/Copy/Paste card, not the stock Windows menu.
    /// Only the two password boxes and the color picker's hex box had it; the appointment editor's
    /// fields, the add rows and the search and sentence boxes all showed the plain grey menu.</summary>
    public sealed class TextInputMenuTests
    {
        [Fact]
        public void LoadedBoxesTakeTheFamilyMenuAndKeepTheirOwn()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    TextInputMenu.Register();
                    var plain = new TextBox();
                    var password = new PasswordBox();
                    var rich = new RichTextBox();
                    var own = new ContextMenu();
                    var custom = new TextBox { ContextMenu = own };
                    foreach (var box in new FrameworkElement[] { plain, password, rich, custom })
                        box.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, box));

                    foreach (var box in new FrameworkElement[] { plain, password, rich })
                        Assert.NotEqual(DependencyProperty.UnsetValue, box.ReadLocalValue(FrameworkElement.ContextMenuProperty));
                    Assert.Same(own, custom.ContextMenu);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void AppRegistersTheMenuAtStartup()
        {
            var source = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "App.xaml.cs"));
            Assert.Contains("Controls.TextInputMenu.Register();", source);
        }

        private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "")
            => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, ".."));
    }
}
