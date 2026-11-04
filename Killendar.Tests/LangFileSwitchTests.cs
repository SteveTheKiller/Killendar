using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class LangFileSwitchTests
    {
        [Fact]
        public void TheSwitchAndItsValueAreTakenAndTheCalendarIsLeft()
        {
            var (path, remaining) = LangFileSwitch.Take(["--lang-file", "x.xaml", "a.kcal"]);

            Assert.Equal("x.xaml", path);
            Assert.Equal(new[] { "a.kcal" }, remaining);
        }

        [Fact]
        public void ABareCalendarPathPassesThroughUntouched()
        {
            var (path, remaining) = LangFileSwitch.Take(["a.kcal"]);

            Assert.Null(path);
            Assert.Equal(new[] { "a.kcal" }, remaining);
        }

        [Fact]
        public void TheSwitchMatchesAnyCaseAfterOtherArguments()
        {
            var (path, remaining) = LangFileSwitch.Take(["a.kcal", "--LANG-FILE", @"C:\t\de.xaml"]);

            Assert.Equal(@"C:\t\de.xaml", path);
            Assert.Equal(new[] { "a.kcal" }, remaining);
        }

        [Fact]
        public void ATrailingSwitchWithoutAValueIsDropped()
        {
            var (path, remaining) = LangFileSwitch.Take(["a.kcal", "--lang-file"]);

            Assert.Null(path);
            Assert.Equal(new[] { "a.kcal" }, remaining);
        }
    }
}
