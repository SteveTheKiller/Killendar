using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class AppointmentLocaleRegressionTests
    {
        public static IEnumerable<object[]> Cultures => Enum.GetValues(typeof(Locale)).Cast<Locale>()
            .Select(locale => new object[] { LocaleManager.CultureFor(locale).Name });

        [Theory]
        [MemberData(nameof(Cultures))]
        public void EditorLoadSaveAndNewAtPreserveHoursInEveryShippedCulture(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            var style = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                DateFormatManager.Apply(DateStyle.Iso);
                foreach (int minute in new[] { 30, 690, 750, 930, 1410 })
                {
                    var start = new DateTime(2026, 11, 12).AddMinutes(minute);
                    var original = new CalendarEvent { Title = "Existing", Start = start, End = start.AddHours(1) };
                    fixture.Store.Add(original);
                    fixture.Editor.Load(original);
                    fixture.Editor.Save();
                    Assert.Null(fixture.View.Error);
                    var saved = fixture.Store.GetById(original.Id)!;
                    Assert.Equal(start, saved.Start);
                    Assert.Equal(start.AddHours(1), saved.End);
                    fixture.Editor.NewAt(start);
                    fixture.View.FieldTitle = "New " + minute;
                    fixture.Editor.Save();
                    Assert.Null(fixture.View.Error);
                    var added = fixture.Store.Events.Single(e => e.Title == "New " + minute);
                    Assert.Equal(start, added.Start);
                    Assert.Equal(start.AddHours(1), added.End);
                }
            }
            finally { CultureInfo.CurrentCulture = previous; DateFormatManager.Apply(style); }
        }

        [Theory]
        [InlineData("en-US", "de-DE")]
        [InlineData("de-DE", "en-US")]
        public void LiveLanguageChangePreservesTypedHours(string before, string after)
        {
            var previous = CultureInfo.CurrentCulture;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(before);
                var start = new DateTime(2026, 11, 12, 15, 30, 0);
                fixture.Editor.NewAt(start);
                fixture.View.FieldTitle = "Live change";
                CultureInfo.CurrentCulture = new CultureInfo(after);
                fixture.Editor.RefreshLocalizedText();
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);
                Assert.Equal(start, Assert.Single(fixture.Store.Events).Start);
                Assert.Equal(start.AddHours(1), Assert.Single(fixture.Store.Events).End);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }

    internal sealed class AppointmentFixture : IDisposable
    {
        private readonly string _root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation", Guid.NewGuid().ToString("N"));
        internal readonly EventStore Store = new EventStore();
        internal readonly AppointmentTestView View = new AppointmentTestView();
        internal readonly AppointmentEditor Editor;
        internal AppointmentFixture()
        {
            Directory.CreateDirectory(_root);
            Store.Open(Path.Combine(_root, "calendar.kcal"));
            Editor = new AppointmentEditor(View, Store);
        }
        public void Dispose() { Store.Close(); Directory.Delete(_root, true); }
    }

    internal sealed class AppointmentTestView : IAppointmentView
    {
        public Window Window => null!;
        public string? Error { get; private set; }
        public bool Closed { get; private set; }
        public string Loc(string key) => key;
        public void SetStatus(string text) { }
        public string FieldTitle { get; set; } = "";
        public string FieldLocation { get; set; } = "";
        public string FieldDescription { get; set; } = "";
        public string FieldAttendees { get; set; } = "";
        public string FieldCategories { get; set; } = "";
        public string FieldStartDate { get; set; } = "";
        public string FieldStartTime { get; set; } = "";
        public string FieldEndDate { get; set; } = "";
        public string FieldEndTime { get; set; } = "";
        public RepeatFreq FieldRepeat { get; set; }
        public int FieldRepeatEveryN { get; set; } = 1;
        public List<DayOfWeek> FieldRepeatDays { get; set; } = new List<DayOfWeek>();
        public int FieldRepeatCount { get; set; }
        public DateTime? FieldRepeatUntil { get; set; }
        public void ResetRepeat() { FieldRepeat = RepeatFreq.None; FieldRepeatUntil = null; FieldRepeatCount = 0; }
        public bool RepeatSectionVisible { private get; set; }
        public bool SeriesScopeVisible { private get; set; }
        public bool EditWholeSeries { get; set; }
        public bool RepeatEndIncomplete => false;
        public string Heading { private get; set; } = "";
        public bool CanDelete { private get; set; }
        public string DateHint { private get; set; } = "";
        public void SetAllDay(string caption, bool timesVisible) { }
        public void ShowError(string message) => Error = message;
        public void ClearError() => Error = null;
        public void Focus(AppointmentField field, bool selectAll = false) { }
        public void OpenPanel() => Closed = false;
        public void ClosePanel() => Closed = true;
        public void HighlightSelection(DateTime? day, TimeSpan? timeOfDay) { }
    }
}
