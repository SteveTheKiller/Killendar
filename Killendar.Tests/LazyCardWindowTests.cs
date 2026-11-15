using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Killendar.Models;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Window test: opens a chip's card through WPF's own context menu service (the menu
    /// key, sent through InputManager), so it proves the service shows the card built in
    /// ContextMenuOpening and not the placeholder it found first. LazyCardTests only raises the
    /// event by hand, which cannot tell the two apart.</summary>
    [Collection("Calendar settings")]
    public sealed class LazyCardWindowTests
    {
        [Fact]
        public void TheMenuKeyOpensTheBuiltCardNotThePlaceholder() => OnSta(() =>
        {
            // Shows a real window, so it runs only where it is asked for: a hidden desktop or a
            // lab VM sets KILLENDAR_WINDOW_TESTS=1. A plain local run must not open anything.
            if (Environment.GetEnvironmentVariable("KILLENDAR_WINDOW_TESTS") != "1") return;
            var chip = CalendarChrome.Chip(new CalendarEvent
            {
                Title = "Standup",
                Start = new DateTime(2026, 11, 18, 9, 0, 0),
                End = new DateTime(2026, 11, 18, 9, 30, 0),
            }, _ => { });
            var placeholder = chip.ContextMenu;
            var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { chip } }, ShowActivated = true };
            window.Show();
            Pump();
            Assert.True(chip.Focus(), "The chip did not take focus.");

            var source = PresentationSource.FromVisual(chip)!;
            foreach (var routed in new[] { Keyboard.KeyDownEvent, Keyboard.KeyUpEvent })
            {
                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Apps) { RoutedEvent = routed });
                Pump();
            }

            Assert.NotSame(placeholder, chip.ContextMenu);
            Assert.True(chip.ContextMenu!.IsOpen, "The built card is not the one that opened.");
            Assert.False(placeholder!.IsOpen, "The placeholder opened.");
            chip.ContextMenu.IsOpen = false;
            window.Close();
        });

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    TestApplication.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The lazy card window test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
