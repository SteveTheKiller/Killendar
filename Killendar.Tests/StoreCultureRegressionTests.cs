using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class StoreCultureRegressionTests
    {
        [Theory]
        [InlineData("th-TH")]
        [InlineData("fi-FI")]
        [InlineData("ar-SA")]
        public void TimedAndAllDayEventsRoundTripUnderTheRegionCulture(string cultureName)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            RunInCulture(culture, () =>
            {
                string directory = NewDirectory();
                string database = Path.Combine(directory, "test.kcal");
                var store = new EventStore();
                try
                {
                    store.Open(database);
                    store.Add(new CalendarEvent
                    {
                        Title = "Timed",
                        Start = new DateTime(2026, 3, 1, 10, 0, 0),
                        End = new DateTime(2026, 3, 1, 11, 0, 0),
                    });
                    store.Add(new CalendarEvent
                    {
                        Title = "AllDay",
                        AllDay = true,
                        Start = new DateTime(2026, 3, 1),
                        End = new DateTime(2026, 3, 1),
                    });
                    store.Close();
                    store.Open(database);

                    Assert.Equal(new DateTime(2026, 3, 1, 10, 0, 0), store.Events.Single(e => e.Title == "Timed").Start);
                    Assert.Equal(new DateTime(2026, 3, 1), store.Events.Single(e => e.Title == "AllDay").Start.Date);
                }
                finally
                {
                    store.Close();
                    Directory.Delete(directory, true);
                }
            });
        }

        [Fact]
        public void TimedRowWrittenWithADotTimeSeparatorStillLoads()
        {
            RunInCulture(CultureInfo.InvariantCulture, () =>
            {
                string directory = NewDirectory();
                string database = Path.Combine(directory, "test.kcal");
                var store = new EventStore();
                try
                {
                    store.Open(database);
                    store.Add(new CalendarEvent
                    {
                        Title = "Dotted",
                        Start = new DateTime(2026, 3, 1, 10, 0, 0),
                        End = new DateTime(2026, 3, 1, 11, 0, 0),
                    });
                    store.Close();
                    using (var db = new SqliteConnection("Data Source=" + database + ";Pooling=False"))
                    {
                        db.Open();
                        using var cmd = db.CreateCommand();
                        cmd.CommandText = "UPDATE events SET start_utc = '2026-03-01T10.30.00.0000000Z', " +
                                          "end_utc = '2026-03-01T11.30.00.0000000Z'";
                        cmd.ExecuteNonQuery();
                    }
                    store.Open(database);

                    var loaded = store.Events.Single();
                    Assert.Equal(new DateTime(2026, 3, 1, 10, 30, 0, DateTimeKind.Utc).ToLocalTime(), loaded.Start);
                    Assert.Equal(new DateTime(2026, 3, 1, 11, 30, 0, DateTimeKind.Utc).ToLocalTime(), loaded.End);
                }
                finally
                {
                    store.Close();
                    Directory.Delete(directory, true);
                }
            });
        }

        private static string NewDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarCultureTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void RunInCulture(CultureInfo culture, Action body)
        {
            var current = CultureInfo.CurrentCulture;
            var thread = CultureInfo.DefaultThreadCurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                body();
            }
            finally
            {
                CultureInfo.CurrentCulture = current;
                CultureInfo.DefaultThreadCurrentCulture = thread;
            }
        }
    }
}
