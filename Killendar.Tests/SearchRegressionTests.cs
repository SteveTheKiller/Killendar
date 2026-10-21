using System;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // S12: search reads the open calendar in memory - no index file beside an encrypted one -
    // across title, location, description, attendees and categories, with a date span filter.
    [Collection("Calendar settings")]
    public sealed class SearchRegressionTests
    {
        private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0);

        private static CalendarEvent At(string title, DateTime start) => new()
            { Title = title, Start = start, End = start.AddHours(1) };

        [Fact]
        public void EveryWordMustMatchSomeField()
        {
            using var fixture = new AppointmentFixture();
            var dentist = At("Dentist", Now.AddDays(2));
            dentist.Location = "Main Street";
            fixture.Store.Add(dentist);
            var review = At("Review", Now.AddDays(3));
            review.Description = "Bring the dentist forms";
            review.Attendees = ["sam@example.com"];
            review.Categories = "Work";
            fixture.Store.Add(review);

            Assert.Equal(["Dentist", "Review"], fixture.Store.Search("dentist", SearchSpan.Any, Now).Select(e => e.Title));
            Assert.Equal(["Dentist"], fixture.Store.Search("dentist main", SearchSpan.Any, Now).Select(e => e.Title));
            Assert.Equal(["Review"], fixture.Store.Search("sam work", SearchSpan.Any, Now).Select(e => e.Title));
            Assert.Empty(fixture.Store.Search("   ", SearchSpan.Any, Now));
        }

        [Fact]
        public void MatchingIgnoresCaseAndAccents()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(At("Café Müller", Now.AddDays(1)));

            Assert.Single(fixture.Store.Search("cafe muller", SearchSpan.Any, Now));
        }

        [Fact]
        public void SpanFiltersAndOrdersUpcomingThenPast()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(At("Call later", Now.AddDays(5)));
            fixture.Store.Add(At("Call soon", Now.AddDays(1)));
            fixture.Store.Add(At("Call yesterday", Now.AddDays(-1)));
            fixture.Store.Add(At("Call last month", Now.AddDays(-30)));

            Assert.Equal(["Call soon", "Call later"], fixture.Store.Search("call", SearchSpan.Upcoming, Now).Select(e => e.Title));
            Assert.Equal(["Call yesterday", "Call last month"], fixture.Store.Search("call", SearchSpan.Past, Now).Select(e => e.Title));
            Assert.Equal(["Call soon", "Call later", "Call yesterday", "Call last month"],
                fixture.Store.Search("call", SearchSpan.Any, Now).Select(e => e.Title));
        }

        [Fact]
        public void RepeatingAppointmentShowsOnceAtItsNextDate()
        {
            using var fixture = new AppointmentFixture();
            var weekly = At("Team sync", new DateTime(2026, 9, 1, 10, 0, 0));
            weekly.Repeat = RepeatFreq.Weekly;
            fixture.Store.Add(weekly);

            var upcoming = Assert.Single(fixture.Store.Search("sync", SearchSpan.Upcoming, Now));
            Assert.Equal(new DateTime(2026, 10, 27, 10, 0, 0), upcoming.Start);
            var past = Assert.Single(fixture.Store.Search("sync", SearchSpan.Past, Now));
            Assert.Equal(new DateTime(2026, 10, 20, 10, 0, 0), past.Start);
        }

        [Theory]
        [InlineData("7", 7)]
        [InlineData("365", 365)]
        [InlineData("13", 60)]
        [InlineData("", 60)]
        [InlineData(null, 60)]
        public void AgendaHorizonIsAKnownChoiceOrSixtyDays(string? setting, int days)
            => Assert.Equal(days, Views.AgendaView.HorizonDays(setting));
    }
}
