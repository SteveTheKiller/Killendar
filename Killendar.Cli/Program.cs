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
        private const string AgendaUsage = "killendar agenda <yyyy-MM-dd> <days 1..31> [--limit 1..100] [--database absolute.kcal]";
        private const string CreateUsage = "killendar create <json> (requires an open, unlocked Killendar)";

        public static int Main(string[] args)
        {
            if (args.Length > 1 && args[args.Length - 1] == "--json")
                args = args.Take(args.Length - 1).ToArray();
            if (args.Length >= 1 && (args[0] == "--help" || args[0] == "help"))
                return Help(args);
            if (args.Length == 2 && args[0] == "create")
            {
                try
                {
                    var response = CalendarCommandPipe.Send(args[1]);
                    using var result = JsonDocument.Parse(response);
                    if (result.RootElement.TryGetProperty("error", out var error))
                    {
                        Console.Error.WriteLine(error.GetString());
                        return 1;
                    }
                    Console.WriteLine(response);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }
            if (args.Length < 3 || args[0] != "agenda"
                || !DateTime.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var from)
                || !int.TryParse(args[2], out int days) || days < 1 || days > 31)
                return Usage(args.Length > 0 && args[0] == "create" ? CreateUsage : AgendaUsage);
            int limit = 50;
            string? database = null;
            for (int i = 3; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) return Usage(AgendaUsage);
                if (args[i] == "--limit" && int.TryParse(args[i + 1], out int count) && count >= 1 && count <= 100)
                    limit = count;
                else if (args[i] == "--database" && Path.IsPathRooted(args[i + 1]))
                    database = args[i + 1];
                else return Usage(AgendaUsage);
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

        private static int Usage(string usage)
        {
            Console.Error.WriteLine("Usage: " + usage);
            return 2;
        }

        private static int Help(string[] args)
        {
            if (args.Length == 1)
            {
                Console.WriteLine(AgendaUsage);
                Console.WriteLine(CreateUsage);
                Console.WriteLine("Output is JSON by default; an optional trailing --json is accepted.");
                return 0;
            }
            if (args.Length == 2 && args[1] == "agenda")
            {
                Console.WriteLine(AgendaUsage);
                Console.WriteLine("Lists the appointments of the days from the given date, with repeats expanded, as JSON.");
                Console.WriteLine("--limit caps the items returned (default 50). --database reads another .kcal file instead of the active Killendar.");
                return 0;
            }
            if (args.Length == 2 && args[1] == "create")
            {
                Console.WriteLine(CreateUsage);
                Console.WriteLine("Adds an appointment, given as JSON with title, start and end, to the open Killendar.");
                return 0;
            }
            Console.Error.WriteLine("No help for \"" + string.Join(" ", args.Skip(1)) + "\". The commands are agenda and create.");
            return Usage(AgendaUsage + Environment.NewLine + CreateUsage);
        }

        public static object[] Agenda(string database, DateTime from, int days, int limit)
        {
            if (days < 1 || days > 31 || limit < 1 || limit > 100)
                throw new ArgumentException("Invalid agenda bounds");
            var store = new EventStore();
            string? savedDays = null, savedHours = null;
            try
            {
                store.OpenReadOnly(database);
                // The CLI never runs App startup, so the saved work week and the holiday calendar are loaded
                // here; a repeat set to move off days off would otherwise expand against Monday to Friday.
                savedDays = WorkSchedule.FormatDays(WorkSchedule.Days);
                savedHours = WorkSchedule.FormatHours(WorkSchedule.StartHour, WorkSchedule.EndHour);
                WorkSchedule.Load(Settings.Get(WorkSchedule.DaysSetting), Settings.Get(WorkSchedule.HoursSetting));
                Holidays.Load(store, database);
                // GetInRange puts every all-day item of the range first; the agenda reads day by day,
                // so the limit must cut the latest days, not the earliest timed ones.
                return [.. store.GetInRange(from.Date, from.Date.AddDays(days))
                    .OrderBy(ev => ev.Start.Date).ThenBy(ev => ev.AllDay ? 0 : 1).ThenBy(ev => ev.Start)
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
            finally
            {
                store.Close();
                Holidays.Close();
                if (savedDays != null) WorkSchedule.Load(savedDays, savedHours);
            }
        }
    }
}
