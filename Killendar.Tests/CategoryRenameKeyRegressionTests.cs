using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Delete and F2 typed into the inline category rename box belong to the box. The
    /// list's PreviewKeyDown sees them first, and used to open the delete prompt or a second
    /// rename box on top of the first.</summary>
    public sealed class CategoryRenameKeyRegressionTests
    {
        [Theory]
        [InlineData(Key.Delete)]
        [InlineData(Key.F2)]
        public void KeysTypedInTheRenameBoxAreLeftToTheBox(Key key) => OnSta(() =>
            Assert.Equal(CategoryListKey.None, CategoryListKeys.Decide(key, new TextBox())));

        [Fact]
        public void DeleteOnTheListDeletesTheCategory() => OnSta(() =>
            Assert.Equal(CategoryListKey.Delete, CategoryListKeys.Decide(Key.Delete, new ListBoxItem())));

        [Fact]
        public void F2OnTheListRenamesTheCategory() => OnSta(() =>
            Assert.Equal(CategoryListKey.Rename, CategoryListKeys.Decide(Key.F2, new ListBoxItem())));

        [Fact]
        public void OtherKeysOnTheListDoNothing() => OnSta(() =>
            Assert.Equal(CategoryListKey.None, CategoryListKeys.Decide(Key.A, new ListBoxItem())));

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The key decision test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
