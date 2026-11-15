using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Killendar.Models;
using Killendar.Shell;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The window's modifier chords fire only for the modifiers they declare, and the
    /// appointment keys act on the appointment the user is looking at.</summary>
    [Collection("Calendar settings")]
    public sealed class ChordRoutingRegressionTests
    {
        // GF-003: Ctrl+Shift+Z undid and Ctrl+Shift+I imported. Ctrl+Shift+M is the one declared
        // Ctrl+Shift chord and is checked on its own before the plain Ctrl shortcuts.
        [Fact]
        public void PlainCtrlShortcutsIgnoreCtrlShift()
        {
            Assert.True(ShortcutKeyPolicy.PlainCtrl(ModifierKeys.Control));
            Assert.False(ShortcutKeyPolicy.PlainCtrl(ModifierKeys.Control | ModifierKeys.Shift));
            Assert.False(ShortcutKeyPolicy.PlainCtrl(ModifierKeys.Control | ModifierKeys.Alt));
        }

        // GF-002: Alt+A and Alt+R also fired with Shift or Ctrl held.
        [Theory]
        [InlineData(ModifierKeys.Alt | ModifierKeys.Shift)]
        [InlineData(ModifierKeys.Alt | ModifierKeys.Control)]
        public void EditorAltChordsNeedExactlyAlt(ModifierKeys modifiers)
        {
            Assert.Equal(EditorAltAction.None, ShortcutKeyPolicy.EditorAlt(Key.A, modifiers, true));
            Assert.Equal(EditorAltAction.None, ShortcutKeyPolicy.EditorAlt(Key.R, modifiers, true));
            Assert.Equal(EditorAltAction.AllDay, ShortcutKeyPolicy.EditorAlt(Key.A, ModifierKeys.Alt, true));
        }

        // GF-001: Alt+R opened the repeat menu while editing one occurrence, where the button is
        // hidden, so a pattern could be put on a single date.
        [Fact]
        public void AltROnlyOpensTheRepeatMenuWhenTheButtonIsThere()
        {
            Assert.Equal(EditorAltAction.None, ShortcutKeyPolicy.EditorAlt(Key.R, ModifierKeys.Alt, false));
            Assert.Equal(EditorAltAction.Repeat, ShortcutKeyPolicy.EditorAlt(Key.R, ModifierKeys.Alt, true));
        }

        // UI-004: the card shows Ctrl+D and Ctrl+Delete, and those keys must run the card's own
        // Duplicate and Delete paths on the chip's appointment, not the editor's.
        [Fact]
        public void TheAppointmentKeysRunTheCardPaths()
        {
            var ev = new CalendarEvent { Title = "Dentist", Start = new DateTime(2026, 11, 16, 9, 0, 0), End = new DateTime(2026, 11, 16, 10, 0, 0) };
            CalendarEvent? deleted = null, copied = null;
            var keepDelete = CalendarChrome.DeleteRequested;
            var keepDuplicate = CalendarChrome.DuplicateRequested;
            try
            {
                CalendarChrome.DeleteRequested = e => deleted = e;
                CalendarChrome.DuplicateRequested = e => copied = e;
                var duplicate = CalendarChrome.AppointmentKeyAction(Key.D, ModifierKeys.Control);
                var delete = CalendarChrome.AppointmentKeyAction(Key.Delete, ModifierKeys.Control);
                Assert.NotNull(duplicate);
                Assert.NotNull(delete);
                duplicate!(ev);
                delete!(ev);
                Assert.Null(CalendarChrome.AppointmentKeyAction(Key.D, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.Null(CalendarChrome.AppointmentKeyAction(Key.D, ModifierKeys.None));
            }
            finally
            {
                CalendarChrome.DeleteRequested = keepDelete;
                CalendarChrome.DuplicateRequested = keepDuplicate;
            }
            Assert.Same(ev, copied);
            Assert.Same(ev, deleted);
        }

        [Fact]
        public void AFocusedChipNamesItsOwnAppointment() => OnSta(() =>
        {
            var mine = new CalendarEvent { Title = "Mine", Start = new DateTime(2026, 11, 16, 9, 0, 0), End = new DateTime(2026, 11, 16, 10, 0, 0) };
            var theirs = new CalendarEvent { Title = "Theirs", Start = mine.Start, End = mine.End, SourceCalendar = "Work", SourceColor = "#3366CC" };
            Assert.Same(mine, ShortcutKeyPolicy.FocusedAppointment(CalendarChrome.Chip(mine, _ => { })));
            Assert.Null(ShortcutKeyPolicy.FocusedAppointment(CalendarChrome.Chip(theirs, _ => { })));
            Assert.Null(ShortcutKeyPolicy.FocusedAppointment(null));
        });

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
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The chord routing test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
