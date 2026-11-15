using System;
using System.Linq;
using Killendar.Controls;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GK-001, GK-002, GK-003, GK-005, GK-006: the file dialog's decisions, tested at the seams that need no window.</summary>
    public sealed class KitFileDialogSeamTests
    {
        private static object? Call(string name, params object?[] args) => KitTestHost.CallStatic(typeof(FileDialog), name, args);

        // ---- GK-001 ----

        [Fact]
        public void ATypedWildcardIsAppendedAfterTheCallersFilters()
        {
            var first = ((int, bool))Call("TypedFilterSlot", 2, 2)!;
            Assert.Equal((2, false), first);
            var again = ((int, bool))Call("TypedFilterSlot", 2, 3)!;
            Assert.Equal((2, true), again);
        }

        [Fact]
        public void TheTypedWildcardNeverMovesTheCallersFilterIndex()
        {
            Assert.Equal(1, Call("FilterIndexAfterSelection", 2, 2, 1));
            Assert.Equal(2, Call("FilterIndexAfterSelection", 1, 2, 1));
            Assert.Equal(1, Call("FilterIndexAfterSelection", 0, 2, 2));
        }

        // ---- GK-002 ----

        [Fact]
        public async System.Threading.Tasks.Task OneDeadPlaceDropsOnlyItself()
        {
            var candidates = new System.Collections.Generic.List<(string? Label, string Path, bool Pinned, bool Check)>
            {
                (null, @"C:\ok-one", true, true),
                (null, @"\\dead\share", true, true),
                ("Quick", @"C:\quick", false, false),
                ("D:  Data", @"D:\", false, true),
            };
            Func<string, bool> exists = path =>
            {
                if (path.StartsWith(@"\\dead")) System.Threading.Thread.Sleep(3000);
                return true;
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var kept = await FileDialog.KeepExistingPlacesAsync(candidates, exists, TimeSpan.FromMilliseconds(400));
            watch.Stop();

            Assert.Equal(new[] { @"C:\ok-one", @"C:\quick", @"D:\" }, kept.Select(p => p.Path).ToArray());
            Assert.True(watch.ElapsedMilliseconds < 2000, "The healthy places waited " + watch.ElapsedMilliseconds + " ms behind the dead one.");
        }

        // ---- GK-005 ----

        [Fact]
        public async System.Threading.Tasks.Task AskingAgainWhileAProbeIsStillBlockedDoesNotStartAnotherThread()
        {
            int calls = 0;
            using var gate = new System.Threading.ManualResetEventSlim();
            Func<bool> work = () => { System.Threading.Interlocked.Increment(ref calls); gate.Wait(10000); return true; };

            await Assert.ThrowsAsync<TimeoutException>(() => FileDialog.OnStaWorker(work, TimeSpan.FromMilliseconds(100), "same"));
            await Assert.ThrowsAsync<TimeoutException>(() => FileDialog.OnStaWorker(work, TimeSpan.FromMilliseconds(100), "same"));
            Assert.Equal(1, calls);

            gate.Set();
            Assert.True(await FileDialog.OnStaWorker(() => true, TimeSpan.FromSeconds(5), "same"));   // the finished one is forgotten
        }

        [Fact]
        public async System.Threading.Tasks.Task WithoutAKeyEveryAskStartsItsOwnWorker()
        {
            int calls = 0;
            using var gate = new System.Threading.ManualResetEventSlim();
            Func<bool> work = () => { System.Threading.Interlocked.Increment(ref calls); gate.Wait(10000); return true; };
            await Assert.ThrowsAsync<TimeoutException>(() => FileDialog.OnStaWorker(work, TimeSpan.FromMilliseconds(100)));
            await Assert.ThrowsAsync<TimeoutException>(() => FileDialog.OnStaWorker(work, TimeSpan.FromMilliseconds(100)));
            Assert.Equal(2, calls);
            gate.Set();
        }

        // ---- GK-006 ----

        [Fact]
        public void SeveralSelectedFilesAreShownAsAQuotedList()
        {
            Assert.Equal("a.txt", Call("SelectionNameText", new System.Collections.Generic.List<string> { "a.txt" }));
            Assert.Equal("\"a.txt\" \"b c.txt\"", Call("SelectionNameText", new System.Collections.Generic.List<string> { "a.txt", "b c.txt" }));
            Assert.Equal("", Call("SelectionNameText", new System.Collections.Generic.List<string>()));
        }

        [Theory]
        [InlineData("", "\"a\" \"b\"", true)]
        [InlineData("\"a\" \"b\"", "\"a\" \"b\"", true)]
        [InlineData("  \"a\" \"b\"  ", "\"a\" \"b\"", true)]
        [InlineData("other.txt", "\"a\" \"b\"", false)]
        [InlineData("\"a\"", "\"a\" \"b\"", false)]
        public void TheHighlightedFilesAnswerOnlyWhileTheBoxStillShowsThem(string typed, string shown, bool answers) =>
            Assert.Equal(answers, Call("SelectionAnswers", typed, shown));

        // ---- GK-003 ----

        [Theory]
        [InlineData(@"C:\Users", true)]
        [InlineData(@"\\server\share\dir", true)]
        [InlineData(@"relative\dir", false)]
        [InlineData(@"..\up", false)]
        [InlineData(@"dir", false)]
        [InlineData(@"C:dir", false)]
        [InlineData("", false)]
        public void OnlyAFullyQualifiedSeedDirectoryIsUsed(string path, bool expected) =>
            Assert.Equal(expected, Call("IsUsableSeedDirectory", path));
    }
}
