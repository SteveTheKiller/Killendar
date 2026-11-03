using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Collections.Generic;
using Killendar.Controls;
using Killendar.Shell;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Alt+Left / Alt+Right must move an all-day appointment by a whole day either way.</summary>
    [Collection("Calendar settings")]
    public sealed class AllDayNudgeRegressionTests
    {
        [Theory]
        [InlineData(-1, 2026, 11, 11, 2026, 11, 11)]
        [InlineData(1, 2026, 11, 13, 2026, 11, 13)]
        public void AllDayAppointmentMovesByWholeDaysInBothDirections(
            int days, int sy, int sm, int sd, int ey, int em, int ed)
        {
            var previous = CultureInfo.CurrentCulture;
            var style = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                DateFormatManager.Apply(DateStyle.US);
                fixture.Editor.NewAt(new DateTime(2026, 11, 12, 15, 30, 0));
                fixture.Editor.ToggleAllDay();
                fixture.Editor.Nudge(TimeSpan.FromDays(days));
                Assert.Equal(DateFormatManager.Format(new DateTime(sy, sm, sd)), fixture.View.FieldStartDate);
                Assert.Equal(DateFormatManager.Format(new DateTime(ey, em, ed)), fixture.View.FieldEndDate);
            }
            finally { CultureInfo.CurrentCulture = previous; DateFormatManager.Apply(style); }
        }
    }

    /// <summary>A location the app fills in (template, load, clear) is not the user typing, so it
    /// must never start the opt-in Photon lookup.</summary>
    public sealed class LocationLookupPolicyTests
    {
        [Fact]
        public void ProgrammaticFillNeverStartsALookup() =>
            Assert.False(LocationLookupPolicy.ShouldStart(true, true, true, true, "1600 Pennsylvania Ave"));

        [Fact]
        public void TypingAnAddressStartsALookupWhenEverythingIsOn() =>
            Assert.True(LocationLookupPolicy.ShouldStart(false, true, true, true, "1600 Pennsylvania Ave"));

        [Fact]
        public void TypingDoesNotLookUpWhenOffClosedHiddenOrTooShort()
        {
            Assert.False(LocationLookupPolicy.ShouldStart(false, false, true, true, "abc"));
            Assert.False(LocationLookupPolicy.ShouldStart(false, true, false, true, "abc"));
            Assert.False(LocationLookupPolicy.ShouldStart(false, true, true, false, "abc"));
            Assert.False(LocationLookupPolicy.ShouldStart(false, true, true, true, " ab "));
        }
    }

    /// <summary>The title bars drag only while the button is still down; a touch tap or fast click
    /// would otherwise throw from DragMove.</summary>
    public sealed class TitleBarDragTests
    {
        [Fact]
        public void DragsOnlyWhileTheButtonIsPressed()
        {
            Assert.True(TitleBarDrag.ShouldDrag(MouseButtonState.Pressed));
            Assert.False(TitleBarDrag.ShouldDrag(MouseButtonState.Released));
        }
    }

    /// <summary>Category chips are real toggle buttons: focusable, named, and Click-driven.</summary>
    public sealed class CategoryChipTests
    {
        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The chip test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [Fact]
        public void ChipIsAFocusableNamedToggleThatReflectsAssignment() => OnSta(() =>
        {
            var chip = CategoryChips.Create("Work", () => { });
            CategoryChips.Paint(chip, "Work", true, new List<string> { "Work" });
            Assert.True(chip.Focusable);
            Assert.True(chip.IsTabStop);
            Assert.Equal("Work", AutomationProperties.GetName(chip));
            Assert.True(chip.IsChecked);
            CategoryChips.Paint(chip, "Work", false, new List<string>());
            Assert.False(chip.IsChecked);
        });

        [Fact]
        public void ClickRaisesTheToggleCallbackOnce() => OnSta(() =>
        {
            int clicks = 0;
            var chip = CategoryChips.Create("Work", () => clicks++);
            chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(1, clicks);
        });

        [Fact]
        public void ToggleAddsThenRemovesAndKeepsAssignmentOrder()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            CategoryChips.Toggle(set, order, "Work");
            CategoryChips.Toggle(set, order, "Home");
            Assert.Equal(new[] { "Work", "Home" }, order);
            CategoryChips.Toggle(set, order, "work");
            Assert.Equal(new[] { "Home" }, order);
            Assert.DoesNotContain("Work", set);
        }

        [Fact]
        public void EmptyStateHintIsAFocusableButtonThatOpensManageCategories() => OnSta(() =>
        {
            int opened = 0;
            var hint = CategoryChips.CreateHint(() => opened++);
            Assert.True(hint.Focusable);
            // The screen-reader name follows the language: it is the hint's own translated text.
            var resources = new ResourceDictionary { { "Str_Hint_NoCategories", "Keine Kategorien" } };
            hint.Resources.MergedDictionaries.Add(resources);
            Assert.Equal("Keine Kategorien", AutomationProperties.GetName(hint));
            hint.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(1, opened);
        });
    }
}
