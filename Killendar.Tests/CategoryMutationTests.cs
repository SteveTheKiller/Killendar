using System;
using System.IO;
using System.Linq;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class CategoryMutationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedCategoryRewriteRollsBackDefinitionAndEveryAppointment(bool delete)
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "calendar.kcal");
            var store = new EventStore();
            try
            {
                store.Open(path);
                var day = new DateTime(2026, 1, 5, 9, 0, 0);
                foreach (string title in new[] { "First", "Second" })
                    store.Add(new CalendarEvent { Title = title, Start = day, End = day.AddHours(1), Categories = "Red, Blue" });
                Execute(path, "CREATE TRIGGER reject_second BEFORE UPDATE ON events WHEN OLD.title = 'Second' BEGIN SELECT RAISE(ABORT, 'second write fault'); END");
                if (delete) store.DeleteCategory("Red"); else store.RenameCategory("Red", "Important");
                Assert.NotNull(store.LoadError);
                Assert.Contains(store.ListCategories(), c => c.Name == "Red");
                Assert.DoesNotContain(store.ListCategories(), c => c.Name == "Important");
                Assert.All(store.GetOnDay(day), e => Assert.Equal("Red, Blue", e.Categories));
                store.Close();
                store.Open(path);
                Assert.Contains(store.ListCategories(), c => c.Name == "Red");
                Assert.All(store.GetOnDay(day), e => Assert.Equal("Red, Blue", e.Categories));
                Execute(path, "DROP TRIGGER reject_second");
                if (delete) store.DeleteCategory("Red"); else store.RenameCategory("Red", "Important");
                Assert.Null(store.LoadError);
                Assert.DoesNotContain(store.ListCategories(), c => c.Name == "Red");
                Assert.All(store.GetOnDay(day), e => Assert.Equal(delete ? "Blue" : "Important, Blue", e.Categories));
                store.Close();
                store.Open(path);
                Assert.All(store.GetOnDay(day), e => Assert.Equal(delete ? "Blue" : "Important, Blue", e.Categories));
            }
            finally { store.Close(); Directory.Delete(root, true); }
        }

        private static void Execute(string path, string sql)
        {
            using var db = new SqliteConnection("Data Source=" + path);
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
