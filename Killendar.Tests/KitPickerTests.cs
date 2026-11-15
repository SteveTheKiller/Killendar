using System;
using System.Threading;
using System.Windows.Threading;
using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GL-003 and GL-004.</summary>
    [Collection("Calendar settings")]
    public sealed class KitPickerTests
    {
        [Theory]
        [InlineData(@"\\", false)]
        [InlineData("//", false)]
        [InlineData(@"\\server", false)]
        [InlineData(@"\\server\", false)]
        [InlineData(@"\\\share", false)]
        [InlineData(@"\\server\share", true)]
        [InlineData(@"\\server\share\file.txt", true)]
        [InlineData(@"C:\folder\file.txt", true)]
        [InlineData(@"C:file.txt", false)]
        public void OnlyAWholeUncShareIsFullyQualified(string typed, bool expected) =>
            Assert.Equal(expected, PickerAcceptRule.CanAccept(false, false, false, false, typed));

        [Fact]
        public void TheIconLoadPostsToTheAppDispatcherOrNowhere()
        {
            // The load is requested only when the app has a UI dispatcher to post the result to. The
            // old code asked Dispatcher.CurrentDispatcher, which on a worker thread is a dispatcher
            // nobody runs. (In a run where another test already made an Application, both agree.)
            bool requested = false, appPresent = false;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    appPresent = System.Windows.Application.Current != null;
                    var place = new PickerPlace("Docs", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                    _ = place.Icon;
                    requested = (bool)typeof(PickerPlace).GetField("_iconRequested",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(place)!;
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
            if (failure != null) throw new InvalidOperationException(failure.ToString());
            Assert.Equal(appPresent, requested);
        }
    }
}
