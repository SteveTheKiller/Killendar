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

        [Theory]
        [InlineData("en-US", "fr-FR", DateStyle.FollowWindows, false)]
        [InlineData("en-US", "fr-FR", DateStyle.FollowWindows, true)]
        [InlineData("de-DE", "en-US", DateStyle.US, false)]
        [InlineData("de-DE", "en-US", DateStyle.US, true)]
        [InlineData("hu-HU", "en-US", DateStyle.EU, false)]
        [InlineData("hu-HU", "en-US", DateStyle.EU, true)]
        public void LiveLanguageChangePreservesAmbiguousDates(string before, string after,
            DateStyle selectedStyle, bool allDay)
        {
            var previous = CultureInfo.CurrentCulture;
            var style = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(before);
                DateFormatManager.Apply(selectedStyle);
                fixture.Editor.NewAt(new DateTime(2026, 11, 12, 15, 30, 0));
                fixture.View.FieldTitle = "Keep November appointment";
                fixture.View.FieldRepeat = Models.RepeatFreq.Daily;
                fixture.View.FieldRepeatUntil = new DateTime(2026, 12, 11);
                if (allDay) fixture.Editor.ToggleAllDay();
                CultureInfo.CurrentCulture = new CultureInfo(after);
                fixture.Editor.RefreshLocalizedText();
                fixture.Editor.Save();
                Assert.Null(fixture.View.Error);
                var saved = Assert.Single(fixture.Store.Events);
                Assert.Equal(allDay ? new DateTime(2026, 11, 12) : new DateTime(2026, 11, 12, 15, 30, 0), saved.Start);
                Assert.Equal(allDay ? new DateTime(2026, 11, 13) : new DateTime(2026, 11, 12, 16, 30, 0), saved.End);
                Assert.Equal(allDay, saved.AllDay);
                Assert.Equal(Models.RepeatFreq.Daily, saved.Repeat);
                Assert.Equal(new DateTime(2026, 12, 11), saved.RepeatUntil);
                Assert.Equal(after, CultureInfo.CurrentCulture.Name);
            }
            finally { CultureInfo.CurrentCulture = previous; DateFormatManager.Apply(style); }
        }

        [Fact]
        public void LiveLanguageChangePreservesIncompleteDateInput()
        {
            var previous = CultureInfo.CurrentCulture;
            var style = DateFormatManager.Current;
            using var fixture = new AppointmentFixture();
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                DateFormatManager.Apply(DateStyle.FollowWindows);
                fixture.Editor.NewAt(new DateTime(2026, 11, 12, 15, 30, 0));
                fixture.View.FieldTitle = "Incomplete dates";
                fixture.View.FieldStartDate = "11/";
                fixture.View.FieldEndDate = "11/";
                fixture.View.RepeatDateText = "12/";
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                fixture.Editor.RefreshLocalizedText();
                Assert.Equal("11/", fixture.View.FieldStartDate);
                Assert.Equal("11/", fixture.View.FieldEndDate);
                Assert.Equal("12/", fixture.View.RepeatDateText);
                fixture.Editor.Save();
                Assert.NotNull(fixture.View.Error);
                Assert.False(fixture.View.Closed);
                Assert.Empty(fixture.Store.Events);
                Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
            }
            finally { CultureInfo.CurrentCulture = previous; DateFormatManager.Apply(style); }
        }
    }

    internal sealed class AppointmentFixture : IDisposable
    {
        private readonly string _root = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
        internal readonly EventStore Store = new EventStore();
        internal readonly AppointmentTestView View = new AppointmentTestView();
        internal readonly AppointmentEditor Editor;
        internal AppointmentFixture()
        {
            Directory.CreateDirectory(_root);
            Store.Open(Path.Combine(_root, "calendar.kcal"));
            Editor = new AppointmentEditor(View, Store);
            View.StartChanged = Editor.StartEdited;
        }
        public void Dispose() { Store.Close(); Directory.Delete(_root, true); }
    }

    internal sealed class AppointmentTestView : IAppointmentView
    {
        public Window Window => null!;
        public string? Error { get; private set; }
        public bool Closed { get; private set; }
        public string Loc(string key) => key;
        internal string LastStatus = "";
        public void SetStatus(string text) => LastStatus = text;
        internal bool DiscardAnswer = true;
        internal DraftChoice? DraftAnswer;
        internal int DiscardQuestions;
        public DraftChoice AskAboutDraft()
        {
            DiscardQuestions++;
            return DraftAnswer ?? (DiscardAnswer ? DraftChoice.Discard : DraftChoice.KeepEditing);
        }
        public string FieldTitle { get; set; } = "";
        public string FieldLocation { get; set; } = "";
        public string FieldDescription { get; set; } = "";
        public string FieldAttendees { get; set; } = "";
        public string FieldCategories { get; set; } = "";
        public int? FieldReminder { get; set; }
        public bool FieldShowAsFree { get; set; }
        public void ShowMeeting(MeetingInfo? meeting) { }
        private string _startDate = "", _startTime = "";
        internal Action? StartChanged;
        public string FieldStartDate
        {
            get => _startDate;
            set { _startDate = value; StartChanged?.Invoke(); }
        }
        public string FieldStartTime
        {
            get => _startTime;
            set { _startTime = value; StartChanged?.Invoke(); }
        }
        public string FieldEndDate { get; set; } = "";
        public string FieldEndTime { get; set; } = "";
        public RepeatFreq FieldRepeat { get; set; }
        public int FieldRepeatEveryN { get; set; } = 1;
        public int FieldRepeatMonthWeek { get; set; }
        public List<DayOfWeek> FieldRepeatDays { get; set; } = new List<DayOfWeek>();
        public int FieldRepeatCount { get; set; }
        public string RepeatDateText { get; set; } = "";
        public DateTime? FieldRepeatUntil
        {
            get => DateFormatManager.TryParse(RepeatDateText, out var date) ? date : (DateTime?)null;
            set => RepeatDateText = value.HasValue ? DateFormatManager.Format(value.Value) : "";
        }
        public void ResetRepeat() { FieldRepeat = RepeatFreq.None; FieldRepeatUntil = null; FieldRepeatCount = 0; }
        public bool RepeatSectionVisible { private get; set; }
        public bool SeriesScopeVisible { private get; set; }
        public bool EditWholeSeries { get; set; }
        public bool EditFollowing { get; set; }
        public bool FollowingOffered { set { } }
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
