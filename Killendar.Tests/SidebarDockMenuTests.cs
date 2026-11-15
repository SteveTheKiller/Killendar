using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Killendar.Models;
using Killendar.Shell;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The sidebar's dock menu (Left / Right) opens only on bare panel space. A right-click
    /// on a day agenda appointment opens that appointment's card: the dock handler marked the
    /// click handled, so WPF never opened the chip's own menu.</summary>
    [Collection("Calendar settings")]
    public sealed class SidebarDockMenuTests
    {
        [Fact]
        public void AnAgendaAppointmentKeepsItsOwnCard() => OnSta(() =>
        {
            var panel = new StackPanel();
            var row = new Grid();
            var chip = CalendarChrome.Chip(new CalendarEvent
            {
                Title = "Morning standup",
                Start = new DateTime(2026, 11, 17, 9, 0, 0),
                End = new DateTime(2026, 11, 17, 9, 15, 0),
            }, _ => { }, compact: false);
            row.Children.Add(chip);
            panel.Children.Add(row);

            Assert.False(MainWindow.SidebarDockMenuApplies(chip.Child, panel));
            Assert.False(MainWindow.SidebarDockMenuApplies(chip, panel));
        });

        [Fact]
        public void BarePanelSpaceStillOpensTheDockMenu() => OnSta(() =>
        {
            var panel = new StackPanel();
            var spacer = new Border();
            panel.Children.Add(spacer);

            Assert.True(MainWindow.SidebarDockMenuApplies(spacer, panel));
            Assert.True(MainWindow.SidebarDockMenuApplies(panel, panel));
        });

        [Fact]
        public void FieldsAndButtonsKeepTheirOwnMenus() => OnSta(() =>
        {
            var panel = new StackPanel();
            var box = new TextBox();
            var button = new Button();
            panel.Children.Add(box);
            panel.Children.Add(button);

            Assert.False(MainWindow.SidebarDockMenuApplies(box, panel));
            Assert.False(MainWindow.SidebarDockMenuApplies(button, panel));
        });

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
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The sidebar menu test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
