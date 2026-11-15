using System;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>GJ-002: a rename that only changes letter case is a real rename.</summary>
    public sealed class KillendarRenameJudgeTests
    {
        private static (string verdict, string name) Judge(string oldName, string raw, Func<string, bool> exists)
        {
            var dialog = typeof(App).Assembly.GetType("Killendar.Controls.KillendarsDialog")!;
            var result = KitTestHost.CallStatic(dialog, "JudgeRename", oldName, raw, exists)!;
            var type = result.GetType();
            return (type.GetField("Item1")!.GetValue(result)!.ToString()!, (string)type.GetField("Item2")!.GetValue(result)!);
        }

        [Fact]
        public void ACaseOnlyChangeGoesAheadEvenThoughTheFileAlreadyExists()
        {
            // On NTFS the "new" name is the same file, so an exists check always says yes.
            var (verdict, name) = Judge("work" + Killendar.Services.EventStore.Extension, "Work", _ => true);
            Assert.Equal("Go", verdict);
            Assert.Equal("Work" + Killendar.Services.EventStore.Extension, name);
        }

        [Fact]
        public void TheSameNameAndEmptyNamesStayRejected()
        {
            string old = "work" + Killendar.Services.EventStore.Extension;
            Assert.Equal("Unchanged", Judge(old, "work", _ => true).verdict);
            Assert.Equal("Unchanged", Judge(old, "  ", _ => false).verdict);
        }

        [Fact]
        public void ADifferentNameThatExistsIsStillRefused()
        {
            Assert.Equal("Exists", Judge("work" + Killendar.Services.EventStore.Extension, "home", _ => true).verdict);
            Assert.Equal("Go", Judge("work" + Killendar.Services.EventStore.Extension, "home", _ => false).verdict);
        }

        [Fact]
        public void AnInvalidNameIsRefused()
        {
            Assert.Equal("Invalid", Judge("work" + Killendar.Services.EventStore.Extension, "a|b", _ => false).verdict);
        }
    }
}
