using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Killendar.Models;

namespace Killendar.Services
{
    public static class CalendarCommands
    {
        private static readonly string[] DateTimeFormats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

        public static string Create(EventStore store, string request)
        {
            try
            {
                using var document = JsonDocument.Parse(request);
                var input = document.RootElement;
                if (!store.IsOpen || input.ValueKind != JsonValueKind.Object ||
                    input.EnumerateObject().Any(p => p.Name != "title" && p.Name != "start" &&
                        p.Name != "end" && p.Name != "allDay" && p.Name != "location" &&
                        p.Name != "description" && p.Name != "categories"))
                    return Error("Open and unlock a Killendar, then provide valid appointment fields.");

                if (!RequiredText(input, "title", 200, out var title) ||
                    !RequiredText(input, "start", 19, out var startText) ||
                    !RequiredText(input, "end", 19, out var endText) ||
                    !OptionalText(input, "location", 500, out var location) ||
                    !OptionalText(input, "description", 4000, out var description) ||
                    !OptionalText(input, "categories", 500, out var categories))
                    return Error("The appointment title, dates, or text fields are invalid.");

                bool allDay = false;
                if (input.TryGetProperty("allDay", out var allDayValue))
                {
                    if (allDayValue.ValueKind != JsonValueKind.True && allDayValue.ValueKind != JsonValueKind.False)
                        return Error("allDay must be true or false.");
                    allDay = allDayValue.GetBoolean();
                }

                string[] formats = allDay ? ["yyyy-MM-dd"] : DateTimeFormats;
                if (!DateTime.TryParseExact(startText, formats, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var start) ||
                    !DateTime.TryParseExact(endText, formats, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var end) || end <= start)
                    return Error(allDay
                        ? "Use start and exclusive end dates in YYYY-MM-DD format."
                        : "Use local start and end times in YYYY-MM-DDTHH:MM format, with end after start.");

                var appointment = new CalendarEvent
                {
                    Title = title!.Trim(), Start = start, End = end, AllDay = allDay,
                    Location = location!.Trim(), Description = description!.Trim(),
                    Categories = EventStore.NormalizeCategories(categories)
                };
                store.Add(appointment);
                if (store.LoadError != null) return Error("Killendar could not save the appointment: " + store.LoadError);
                return JsonSerializer.Serialize(new
                {
                    id = appointment.Id,
                    title = appointment.Title,
                    start = appointment.Start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                    end = appointment.End.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                    allDay = appointment.AllDay
                });
            }
            catch (JsonException) { return Error("The appointment request is not valid JSON."); }
        }

        private static bool RequiredText(JsonElement input, string name, int max, out string? value)
        {
            value = null;
            return input.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(value = field.GetString()) && value!.Length <= max;
        }

        private static bool OptionalText(JsonElement input, string name, int max, out string? value)
        {
            value = "";
            if (!input.TryGetProperty(name, out var field)) return true;
            return field.ValueKind == JsonValueKind.String && (value = field.GetString()) != null && value.Length <= max;
        }

        private static string Error(string message) => JsonSerializer.Serialize(new { error = message });
    }
}
