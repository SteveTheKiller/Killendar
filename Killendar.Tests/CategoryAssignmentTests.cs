using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class CategoryAssignmentTests
    {
        [Theory]
        [InlineData("Work, Personal, Travel", "personal", "Personal, Work, Travel")]
        [InlineData("Work, Personal", "Travel", "Travel, Work, Personal")]
        public void ChangingPrimaryRetainsOtherCategoriesAfterReopening(string assigned, string primary, string expected)
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarCategoryTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "test.kcal");
            var store = new EventStore();
            try
            {
                store.Open(path);
                var appointment = new CalendarEvent
                {
                    Title = "Category check",
                    Start = new DateTime(2026, 10, 7, 9, 0, 0),
                    End = new DateTime(2026, 10, 7, 10, 0, 0),
                    Categories = assigned
                };
                store.Add(appointment);
                appointment.Categories = EventStore.WithPrimaryCategory(appointment.Categories, primary);
                store.Update(appointment);
                store.Close();
                store.Open(path);
                var saved = store.GetInRange(appointment.Start.Date, appointment.Start.Date.AddDays(1)).Single();
                Assert.Equal(expected, saved.Categories);
                Assert.Equal(expected.Split(',')[0], CategoryManager.PrimaryOf(saved));
            }
            finally
            {
                store.Close();
                Directory.Delete(directory, true);
            }
        }
    }
}
