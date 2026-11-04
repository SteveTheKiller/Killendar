using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // VE-002: opening one date of a series must leave the pattern rows hidden. In the real panel
    // every repeat field setter rebuilds the rows and shows them for any pattern, so hiding the
    // section has to be the last of those writes, not one made before the pattern is loaded.
    [Collection("Calendar settings")]
    public sealed class RepeatSectionVisibilityRegressionTests
    {
        private static readonly DateTime First = new(2026, 10, 19, 9, 0, 0);

        [Fact]
        public void LoadingOneDateOfASeriesHidesThePatternLast()
        {
            using var fixture = new RecordingFixture();
            fixture.Store.Add(new CalendarEvent
            {
                Title = "Standup", Start = First, End = First.AddMinutes(30),
                Repeat = RepeatFreq.Weekly, RepeatDays = [DayOfWeek.Monday], RepeatCount = 6,
            });
            var occurrence = fixture.Store.GetInRange(First.Date.AddDays(7), First.Date.AddDays(8)).Single();
            Assert.True(occurrence.IsOccurrence);

            fixture.Editor.Load(occurrence);

            Assert.Equal("RepeatSectionVisible=False", fixture.View.RepeatWrites.Last());
        }

        [Fact]
        public void LoadingAPlainAppointmentShowsThePatternLast()
        {
            using var fixture = new RecordingFixture();
            fixture.Store.Add(new CalendarEvent { Title = "Dentist", Start = First, End = First.AddHours(1) });

            fixture.Editor.Load(fixture.Store.Events.Single());

            Assert.Equal("RepeatSectionVisible=True", fixture.View.RepeatWrites.Last());
        }

        private sealed class RecordingFixture : IDisposable
        {
            private readonly string _root = Path.Combine(TestScratch.Root, Guid.NewGuid().ToString("N"));
            internal readonly EventStore Store = new EventStore();
            internal readonly RepeatRecordingView View = new RepeatRecordingView(new AppointmentTestView());
            internal readonly AppointmentEditor Editor;
            internal RecordingFixture()
            {
                Directory.CreateDirectory(_root);
                Store.Open(Path.Combine(_root, "calendar.kcal"));
                Editor = new AppointmentEditor(View, Store);
            }
            public void Dispose() { Store.Close(); Directory.Delete(_root, true); }
        }
    }

    /// <summary>
    /// Forwards to the plain test view and logs every write that, in the real panel, decides
    /// whether the pattern rows are on screen.
    /// </summary>
    internal sealed class RepeatRecordingView(AppointmentTestView inner) : IAppointmentView
    {
        internal readonly List<string> RepeatWrites = [];
        private void Log(string name, object? value) => RepeatWrites.Add(name + "=" + value);

        public Window Window => inner.Window;
        public string Loc(string key) => inner.Loc(key);
        public void SetStatus(string text) => inner.SetStatus(text);
        public string FieldTitle { get => inner.FieldTitle; set => inner.FieldTitle = value; }
        public string FieldLocation { get => inner.FieldLocation; set => inner.FieldLocation = value; }
        public string FieldDescription { get => inner.FieldDescription; set => inner.FieldDescription = value; }
        public string FieldAttendees { get => inner.FieldAttendees; set => inner.FieldAttendees = value; }
        public string FieldCategories { get => inner.FieldCategories; set => inner.FieldCategories = value; }
        public int? FieldReminder { get => inner.FieldReminder; set => inner.FieldReminder = value; }
        public bool FieldShowAsFree { get => inner.FieldShowAsFree; set => inner.FieldShowAsFree = value; }
        public void ShowMeeting(MeetingInfo? meeting) => inner.ShowMeeting(meeting);
        public string FieldZoneId { get => inner.FieldZoneId; set => inner.FieldZoneId = value; }
        public void ShowZone(bool visible, bool editable) => inner.ShowZone(visible, editable);
        public string FieldStartDate { get => inner.FieldStartDate; set => inner.FieldStartDate = value; }
        public string FieldStartTime { get => inner.FieldStartTime; set => inner.FieldStartTime = value; }
        public string FieldEndDate { get => inner.FieldEndDate; set => inner.FieldEndDate = value; }
        public string FieldEndTime { get => inner.FieldEndTime; set => inner.FieldEndTime = value; }
        public RepeatFreq FieldRepeat
        {
            get => inner.FieldRepeat;
            set { inner.FieldRepeat = value; Log(nameof(FieldRepeat), value); }
        }
        public int FieldRepeatEveryN { get => inner.FieldRepeatEveryN; set => inner.FieldRepeatEveryN = value; }
        public int FieldRepeatMonthWeek
        {
            get => inner.FieldRepeatMonthWeek;
            set { inner.FieldRepeatMonthWeek = value; Log(nameof(FieldRepeatMonthWeek), value); }
        }
        public List<DayOfWeek> FieldRepeatDays
        {
            get => inner.FieldRepeatDays;
            set { inner.FieldRepeatDays = value; Log(nameof(FieldRepeatDays), value.Count); }
        }
        public int FieldRepeatCount
        {
            get => inner.FieldRepeatCount;
            set { inner.FieldRepeatCount = value; Log(nameof(FieldRepeatCount), value); }
        }
        public DateTime? FieldRepeatUntil
        {
            get => inner.FieldRepeatUntil;
            set { inner.FieldRepeatUntil = value; Log(nameof(FieldRepeatUntil), value); }
        }
        public int FieldRepeatShift
        {
            get => inner.FieldRepeatShift;
            set { inner.FieldRepeatShift = value; Log(nameof(FieldRepeatShift), value); }
        }
        public List<DateTime> FieldRepeatExtraDates { get => inner.FieldRepeatExtraDates; set => inner.FieldRepeatExtraDates = value; }
        public void ResetRepeat() { inner.ResetRepeat(); Log(nameof(ResetRepeat), null); }
        public bool RepeatSectionVisible
        {
            set { inner.RepeatSectionVisible = value; Log(nameof(RepeatSectionVisible), value); }
        }
        public bool SeriesScopeVisible { set => inner.SeriesScopeVisible = value; }
        public bool EditWholeSeries { get => inner.EditWholeSeries; set => inner.EditWholeSeries = value; }
        public bool EditFollowing { get => inner.EditFollowing; set => inner.EditFollowing = value; }
        public bool FollowingOffered { set => inner.FollowingOffered = value; }
        public bool RepeatEndIncomplete => inner.RepeatEndIncomplete;
        public string Heading { set => inner.Heading = value; }
        public bool CanDelete { set => inner.CanDelete = value; }
        public string DateHint { set => inner.DateHint = value; }
        public void SetAllDay(string caption, bool timesVisible) => inner.SetAllDay(caption, timesVisible);
        public void ShowError(string message) => inner.ShowError(message);
        public void ClearError() => inner.ClearError();
        public DraftChoice AskAboutDraft() => inner.AskAboutDraft();
        public void Focus(AppointmentField field, bool selectAll = false) => inner.Focus(field, selectAll);
        public void OpenPanel() => inner.OpenPanel();
        public void ClosePanel() => inner.ClosePanel();
        public void HighlightSelection(DateTime? day, TimeSpan? timeOfDay) => inner.HighlightSelection(day, timeOfDay);
    }
}
