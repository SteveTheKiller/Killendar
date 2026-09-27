using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Killendar.Services;
using Microsoft.Data.Sqlite;

namespace Killendar.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine("Killendar CLI: agenda <yyyy-MM-dd> <days 1..31> [--limit 1..100] [--database absolute.kcal]");
                return 0;
            }
            if (args.Length < 3 || args[0] != "agenda"
                || !DateTime.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var from)
                || !int.TryParse(args[2], out int days) || days < 1 || days > 31)
            {
                Console.Error.WriteLine("Expected agenda <yyyy-MM-dd> <days 1..31>");
                return 2;
            }
            int limit = 50;
            string? database = null;
            for (int i = 3; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) return 2;
                if (args[i] == "--limit" && int.TryParse(args[i + 1], out int count) && count >= 1 && count <= 100)
                    limit = count;
                else if (args[i] == "--database" && Path.IsPathRooted(args[i + 1]))
                    database = args[i + 1];
                else return 2;
            }
            try
            {
                var events = Agenda(database ?? EventStore.ActivePath, from, days, limit);
                Console.WriteLine(JsonSerializer.Serialize(events));
                return 0;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 26 || ex.SqliteExtendedErrorCode == 26)
            {
                Console.Error.WriteLine("Killendar cannot open an encrypted or unreadable calendar. Unlock support is not available yet.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Killendar agenda failed: " + ex.Message);
                return 1;
            }
        }

        public static object[] Agenda(string database, DateTime from, int days, int limit)
        {
            if (days < 1 || days > 31 || limit < 1 || limit > 100)
                throw new ArgumentException("Invalid agenda bounds");
            var store = new EventStore();
            try
            {
                store.OpenReadOnly(database);
                return [.. store.GetInRange(from.Date, from.Date.AddDays(days))
                    .Take(limit)
                    .Select(ev => (object)new
                    {
                        id = ev.Id,
                        title = ev.Title,
                        start = ev.Start.ToString("o"),
                        end = ev.End.ToString("o"),
                        allDay = ev.AllDay,
                        location = ev.Location,
                        categories = ev.Categories,
                        recurring = ev.IsSeries || ev.IsOccurrence,
                    })];
            }
            finally { store.Close(); }
        }
    }
}
