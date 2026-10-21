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
        public void DefinitionWriteFailurePreservesCacheAssignmentsAndNotifications(bool delete)
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Category", Start = new DateTime(2026, 1, 5, 9, 0, 0),
                End = new DateTime(2026, 1, 5, 10, 0, 0), Categories = "Red, Blue" });
            string color = CategoryManager.HexOf("Red");
            int changes = 0;
            fixture.Store.Changed += () => changes++;
            Execute(fixture.Store.FilePath, "CREATE TRIGGER reject_definition BEFORE " + (delete ? "DELETE" : "UPDATE") +
                " ON categories WHEN OLD.name = 'Red' BEGIN SELECT RAISE(ABORT, 'definition write fault'); END");

            if (delete) fixture.Store.DeleteCategory("Red"); else fixture.Store.RenameCategory("Red", "Important");

            Assert.NotNull(fixture.Store.LoadError);
            Assert.Equal(1, changes);
            Assert.Equal(color, CategoryManager.HexOf("Red"));
            Assert.Equal(CategoryManager.OrphanHex, CategoryManager.HexOf("Important"));
            Assert.Equal("Red, Blue", Assert.Single(fixture.Store.Events).Categories);
            string path = fixture.Store.FilePath;
            fixture.Store.Close();
            fixture.Store.Open(path);
            Assert.Equal(color, CategoryManager.HexOf("Red"));
            Assert.Equal("Red, Blue", Assert.Single(fixture.Store.Events).Categories);
            Execute(path, "DROP TRIGGER reject_definition");
            changes = 0;

            if (delete) fixture.Store.DeleteCategory("Red"); else fixture.Store.RenameCategory("Red", "Important");

            Assert.Null(fixture.Store.LoadError);
            Assert.Equal(1, changes);
            Assert.Equal(CategoryManager.OrphanHex, CategoryManager.HexOf("Red"));
            Assert.Equal(delete ? "Blue" : "Important, Blue", Assert.Single(fixture.Store.Events).Categories);
            if (!delete) Assert.Equal(color, CategoryManager.HexOf("Important"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedCategoryRewriteRollsBackDefinitionAndEveryAppointment(bool delete)
        {
            string root = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
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

        // BF-011: add and recolor go through the same recoverable boundary as rename and delete.
        [Fact]
        public void FailedCategoryAddIsReportedInsteadOfThrown()
        {
            using var fixture = new AppointmentFixture();
            Execute(fixture.Store.FilePath, "CREATE TRIGGER reject_add BEFORE INSERT ON categories " +
                "BEGIN SELECT RAISE(ABORT, 'add fault'); END");

            bool saved = fixture.Store.AddCategory("Travel", "#123456");

            Assert.False(saved);
            Assert.Contains("add fault", fixture.Store.LoadError);
            Assert.DoesNotContain(fixture.Store.ListCategories(), c => c.Name == "Travel");
            Assert.Equal(CategoryManager.OrphanHex, CategoryManager.HexOf("Travel"));
        }

        [Fact]
        public void FailedRecolorKeepsTheStoredColorEverywhere()
        {
            using var fixture = new AppointmentFixture();
            Assert.True(fixture.Store.AddCategory("Travel", "#123456"));
            Execute(fixture.Store.FilePath, "CREATE TRIGGER reject_color BEFORE UPDATE ON categories " +
                "BEGIN SELECT RAISE(ABORT, 'color fault'); END");
            CategoryManager.Preview("Travel", "#ABCDEF");

            bool saved = fixture.Store.SetCategoryColor("Travel", "#ABCDEF");

            Assert.False(saved);
            Assert.Contains("color fault", fixture.Store.LoadError);
            Assert.Equal("#123456", fixture.Store.ListCategories().Single(c => c.Name == "Travel").Color);
            Assert.Equal("#123456", CategoryManager.HexOf("Travel"), ignoreCase: true);
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
