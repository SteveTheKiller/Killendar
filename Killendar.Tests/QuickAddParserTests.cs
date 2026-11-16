using System;
using System.Globalization;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // Sentence box: the box reads a typed line into the fields the edit panel opens with.
    public sealed class QuickAddParserTests
    {
        // Wednesday 18 November 2026, 10:15.
        private static readonly DateTime Now = new DateTime(2026, 11, 18, 10, 15, 0);
        private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

        private static QuickAddResult Parse(string text, CultureInfo? culture = null)
            => QuickAdd.Parse(text, Now, culture ?? Us, QuickAddWords.English);

        [Fact]
        public void WeekdayTimeAndPlaceFillEveryField()
        {
            var r = Parse("lunch with Dave fri 12:30 at Mel's");
            Assert.Equal("lunch with Dave", r.Title);
            Assert.Equal(new DateTime(2026, 11, 20, 12, 30, 0), r.Start);
            Assert.Equal(new DateTime(2026, 11, 20, 13, 30, 0), r.End);
            Assert.Equal("Mel's", r.Location);
            Assert.False(r.AllDay);
        }

        [Theory]
        [InlineData("call mom tomorrow 3pm", 2026, 11, 19, 15, 0)]
        [InlineData("call mom today at 4:45pm", 2026, 11, 18, 16, 45)]
        [InlineData("standup noon", 2026, 11, 18, 12, 0)]
        [InlineData("dentist 9am", 2026, 11, 19, 9, 0)]
        [InlineData("dentist nov 30 2pm", 2026, 11, 30, 14, 0)]
        [InlineData("dentist 30 november 14:00", 2026, 11, 30, 14, 0)]
        [InlineData("dentist 12/2 8:15", 2026, 12, 2, 8, 15)]
        [InlineData("dentist 1/5/2027 8:15", 2027, 1, 5, 8, 15)]
        [InlineData("gig wed 21:00", 2026, 11, 18, 21, 0)]
        [InlineData("gig next wed 21:00", 2026, 11, 25, 21, 0)]
        [InlineData("budget review jan 3", 2027, 1, 3, 0, 0)]
        public void DatesAndTimesResolve(string text, int y, int mo, int d, int h, int mi)
            => Assert.Equal(new DateTime(y, mo, d, h, mi, 0), Parse(text).Start);

        [Fact]
        public void APastTimeWithNoDateMeansTomorrow()
            => Assert.Equal(new DateTime(2026, 11, 19, 9, 0, 0), Parse("dentist 9am").Start);

        [Theory]
        [InlineData("soundcheck 3-5pm", 15, 17, 0)]
        [InlineData("soundcheck 3pm-5:30pm", 15, 17, 30)]
        [InlineData("soundcheck 15:00-16:15", 15, 16, 15)]
        [InlineData("soundcheck 3pm for 2h", 15, 17, 0)]
        [InlineData("soundcheck 3pm for 90m", 15, 16, 30)]
        [InlineData("soundcheck 3pm for 1.5h", 15, 16, 30)]
        public void RangesAndLengthsSetTheEnd(string text, int startHour, int endHour, int endMinute)
        {
            var r = Parse(text);
            Assert.Equal(new DateTime(2026, 11, 18, startHour, 0, 0), r.Start);
            Assert.Equal(new DateTime(2026, 11, 18, endHour, endMinute, 0), r.End);
            Assert.Equal("soundcheck", r.Title);
        }

        [Fact]
        public void ADateWithNoTimeIsAllDay()
        {
            var r = Parse("mom visiting sat");
            Assert.True(r.AllDay);
            Assert.Equal(new DateTime(2026, 11, 21), r.Start.Date);
            Assert.Equal("mom visiting", r.Title);
        }

        [Fact]
        public void AllDayWordsWin()
        {
            var r = Parse("travel day fri all day");
            Assert.True(r.AllDay);
            Assert.Equal(new DateTime(2026, 11, 20), r.Start.Date);
            Assert.Equal("travel day", r.Title);
        }

        [Fact]
        public void NoDateOrTimeStartsAtTheNextHour()
        {
            var r = Parse("think about setlist");
            Assert.False(r.AllDay);
            Assert.Equal(new DateTime(2026, 11, 18, 11, 0, 0), r.Start);
            Assert.Equal(new DateTime(2026, 11, 18, 12, 0, 0), r.End);
        }

        [Fact]
        public void HashWordsBecomeCategories()
        {
            var r = Parse("rehearsal #band thu 7pm #music");
            Assert.Equal("rehearsal", r.Title);
            Assert.Equal("band, music", r.Categories);
        }

        [Fact]
        public void PlaceStopsAtTheDateAndTime()
        {
            var r = Parse("coffee at Joe's Diner tomorrow 8am");
            Assert.Equal("coffee", r.Title);
            Assert.Equal("Joe's Diner", r.Location);
            Assert.Equal(new DateTime(2026, 11, 19, 8, 0, 0), r.Start);
        }

        [Fact]
        public void WordsThatOnlyLookLikeKeywordsStayInTheTitle()
        {
            var r = Parse("look at the van");
            Assert.Equal("look", r.Title);
            Assert.Equal("the van", r.Location);
            var plain = Parse("format the drive");
            Assert.Equal("format the drive", plain.Title);
            Assert.Equal("", plain.Location);
        }

        [Fact]
        public void DayFirstCulturesReadNumericDatesDayFirst()
        {
            var r = Parse("dentist 2/12 8:15", CultureInfo.GetCultureInfo("en-GB"));
            Assert.Equal(new DateTime(2026, 12, 2, 8, 15, 0), r.Start);
        }

        [Fact]
        public void WeekdayAndMonthNamesComeFromTheCulture()
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var r = QuickAdd.Parse("Probe freitag 19:00", Now, de, QuickAddWords.English);
            Assert.Equal(new DateTime(2026, 11, 20, 19, 0, 0), r.Start);
            Assert.Equal("Probe", r.Title);
            var m = QuickAdd.Parse("Probe 3 dezember", Now, de, QuickAddWords.English);
            Assert.Equal(new DateTime(2026, 12, 3), m.Start.Date);
        }

        [Fact]
        public void LocalizedKeywordsAreRead()
        {
            var words = new QuickAddWords(today: "heute", tomorrow: "morgen", next: "nächsten", at: "in",
                forWord: "für", allDay: "ganztägig", noon: "mittag");
            var r = QuickAdd.Parse("Essen morgen 12:30 in Berlin für 2h", Now, CultureInfo.GetCultureInfo("de-DE"), words);
            Assert.Equal("Essen", r.Title);
            Assert.Equal("Berlin", r.Location);
            Assert.Equal(new DateTime(2026, 11, 19, 12, 30, 0), r.Start);
            Assert.Equal(new DateTime(2026, 11, 19, 14, 30, 0), r.End);
        }

        [Theory]
        [InlineData("dinner 7:30 pm", 19, 30)]
        [InlineData("dinner 3 pm", 15, 0)]
        [InlineData("dinner at 7 pm", 19, 0)]
        public void AnAmOrPmWordJoinsTheNumberBeforeIt(string text, int h, int m)
        {
            var r = Parse(text);
            Assert.Equal(new DateTime(2026, 11, 18, h, m, 0), r.Start);
            Assert.Equal("dinner", r.Title);
            Assert.Equal("", r.Location);
        }

        [Fact]
        public void AtABareHourIsATimeNotAPlace()
        {
            var r = Parse("dinner at 7");
            Assert.Equal("", r.Location);
            Assert.Equal(new DateTime(2026, 11, 18, 19, 0, 0), r.Start);
        }

        [Fact]
        public void ARangeEndWithoutAMarkTakesTheStartsMark()
        {
            var r = Parse("meeting 2pm-3");
            Assert.Equal(new DateTime(2026, 11, 18, 14, 0, 0), r.Start);
            Assert.Equal(new DateTime(2026, 11, 18, 15, 0, 0), r.End);
        }

        [Theory]
        [InlineData("read Chapter 1-2")]
        [InlineData("buy 3.5 kg flour")]
        public void PlainNumbersStayInTheTitle(string text)
            => Assert.Equal(text, Parse(text).Title);

        [Fact]
        public void APlaceKeepsWordsThatLookLikeDays()
        {
            var r = Parse("brunch tomorrow 10am at Sun Cafe");
            Assert.Equal("Sun Cafe", r.Location);
            Assert.Equal(new DateTime(2026, 11, 19, 10, 0, 0), r.Start);
        }

        [Fact]
        public void TodaysWeekdayWithAPastTimeMeansNextWeek()
            => Assert.Equal(new DateTime(2026, 11, 25, 9, 0, 0), Parse("standup wed 9am").Start);

        [Fact]
        public void MultiWordKeywordsAreRead()
        {
            var nb = new QuickAddWords("i dag", "i morgen", "neste", "hos", "i", "hele dagen", "kl. 12");
            var r = QuickAdd.Parse("Lunsj i morgen kl. 12 hos Per", Now, CultureInfo.GetCultureInfo("nb-NO"), nb);
            Assert.Equal("Lunsj", r.Title);
            Assert.Equal("Per", r.Location);
            Assert.Equal(new DateTime(2026, 11, 19, 12, 0, 0), r.Start);
        }

        [Theory]
        [InlineData("party 12/31/9999 11pm for 2h")]
        [InlineData("party 12/31/9999 11pm-1am")]
        [InlineData("party 12/31/9999")]
        public void DatesAtTheEndOfTheCalendarDoNotThrow(string text)
        {
            var r = Parse(text);
            Assert.True(r.End >= r.Start);
        }

        [Fact]
        public void EmptyTextHasNoTitle()
            => Assert.Equal("", Parse("   ").Title);
    }
}
