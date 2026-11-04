using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A bare letter pressed on a focused dropdown or check box in the appointment editor
    /// belongs to that control. It used to reach the calendar shortcuts, so N on the Reminder box
    /// started a new draft and W switched the view.</summary>
    public sealed class BareKeyRoutingRegressionTests
    {
        [Theory]
        [InlineData(Key.N)]
        [InlineData(Key.W)]
        [InlineData(Key.D2)]
        public void LettersOnAnEditorDropdownStayWithTheDropdown(Key key) => OnSta(() =>
            Assert.True(ShortcutKeyPolicy.BareKeyBelongsToFocus(new ComboBox(), true, key)));

        [Fact]
        public void LettersOnAnOpenDropdownItemStayWithTheList() => OnSta(() =>
            Assert.True(ShortcutKeyPolicy.BareKeyBelongsToFocus(new ComboBoxItem(), false, Key.M)));

        [Theory]
        [InlineData(Key.N)]
        [InlineData(Key.Left)]
        [InlineData(Key.Right)]
        public void LettersAndArrowsOnAnEditorCheckBoxStayWithTheCheckBox(Key key) => OnSta(() =>
            Assert.True(ShortcutKeyPolicy.BareKeyBelongsToFocus(new CheckBox(), true, key)));

        [Fact]
        public void FunctionKeysOnAnEditorCheckBoxStillReachTheShortcuts() => OnSta(() =>
            Assert.False(ShortcutKeyPolicy.BareKeyBelongsToFocus(new CheckBox(), true, Key.F1)));

        [Fact]
        public void ACheckBoxOutsideTheEditorLeavesLettersToTheShortcuts() => OnSta(() =>
            Assert.False(ShortcutKeyPolicy.BareKeyBelongsToFocus(new CheckBox(), false, Key.N)));

        [Fact]
        public void TextFieldsStillOwnEveryBareKey() => OnSta(() =>
        {
            Assert.True(ShortcutKeyPolicy.BareKeyBelongsToFocus(new TextBox(), false, Key.N));
            Assert.True(ShortcutKeyPolicy.BareKeyBelongsToFocus(new PasswordBox(), false, Key.F1));
        });

        [Fact]
        public void NothingFocusedLeavesLettersToTheShortcuts()
            => Assert.False(ShortcutKeyPolicy.BareKeyBelongsToFocus(null, false, Key.N));

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The key routing test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
