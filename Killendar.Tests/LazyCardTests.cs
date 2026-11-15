using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Killendar.Models;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A chip or day cell builds its right-click card only when the card opens. Building
    /// every card up front tripled the month render and doubled its memory against 1.4.2.</summary>
    [Collection("Calendar settings")]
    public sealed class LazyCardTests
    {
        [Fact]
        public void AChipBuildsNoCardUntilItsMenuOpens() => OnSta(() =>
        {
            var chip = CalendarChrome.Chip(Event(), _ => { });
            var placeholder = chip.ContextMenu;
            Assert.NotNull(placeholder);
            Assert.Single(placeholder!.Items);

            Open(chip);

            Assert.NotSame(placeholder, chip.ContextMenu);
            Assert.True(chip.ContextMenu!.Items.Count >= 2);
        });

        [Fact]
        public void ChipsShareOnePlaceholderInsteadOfACardEach() => OnSta(() =>
        {
            var a = CalendarChrome.Chip(Event(), _ => { });
            var b = CalendarChrome.Chip(Event(), _ => { });
            Assert.Same(a.ContextMenu, b.ContextMenu);
        });

        [Fact]
        public void TheExtensionRunsOnTheBuiltCard() => OnSta(() =>
        {
            int extended = 0;
            var chip = CalendarChrome.Chip(Event(), _ => { }, extendCard: m => { extended++; m.Items.Add(new MenuItem { Header = "extra" }); });
            Assert.Equal(0, extended);

            Open(chip);
            Open(chip);

            Assert.Equal(1, extended);
            Assert.Equal("extra", ((MenuItem)chip.ContextMenu!.Items[chip.ContextMenu.Items.Count - 1]).Header);
        });

        [Fact]
        public void ADayCellBuildsItsCardOnOpen() => OnSta(() =>
        {
            var cell = new Border();
            var at = new DateTime(2026, 11, 18, 9, 0, 0);
            CalendarChrome.LazyCard(cell, () => CalendarChrome.DayMenu(at, _ => { }));
            var placeholder = cell.ContextMenu;

            Open(cell);

            Assert.NotSame(placeholder, cell.ContextMenu);

            Assert.Equal(2, cell.ContextMenu!.Items.Count);
        });

        // ContextMenuEventArgs has no public constructor; the menu service builds it the same way.
        private static void Open(FrameworkElement element)
        {
            var args = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new object[] { element, true }, null);
            args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
            element.RaiseEvent(args);
        }

        private static CalendarEvent Event() => new()
        {
            Title = "Standup",
            Start = new DateTime(2026, 11, 18, 9, 0, 0),
            End = new DateTime(2026, 11, 18, 9, 30, 0),
        };

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null) new Application();
                    body();
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The lazy card test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
