using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class EventStoreWriteFailureTests
    {
        [Fact]
        public void FailedWritesLeaveTheVisibleCalendarEqualToTheDatabase()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarWriteTests", Guid.NewGuid().ToString("N"));
            string database = Path.Combine(directory, "test.kcal");
            Directory.CreateDirectory(directory);
            var store = new EventStore();
            var original = new CalendarEvent
            {
                Title = "Original",
                Start = new DateTime(2026, 10, 20, 9, 0, 0),
                End = new DateTime(2026, 10, 20, 10, 0, 0),
            };

            try
            {
                store.Open(database);
                store.Add(original);
                store.Close();
                store.OpenReadOnly(database);
                int changes = 0;
                store.Changed += () => changes++;

                store.Add(new CalendarEvent { Title = "Unsaved", Start = original.Start, End = original.End });
                Assert.NotNull(store.LoadError);
                Assert.Equal("Original", Assert.Single(store.Events).Title);

                var edited = store.Events.Single().Clone();
                edited.Title = "Changed but unsaved";
                store.Update(edited);
                Assert.NotNull(store.LoadError);
                Assert.Equal("Original", Assert.Single(store.Events).Title);

                store.Delete(original.Id);
                Assert.NotNull(store.LoadError);
                Assert.Equal("Original", Assert.Single(store.Events).Title);
                Assert.Equal(3, changes);
            }
            finally
            {
                store.Close();
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void FailedEditorSaveKeepsThePanelOpenAndShowsTheError()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarWriteTests", Guid.NewGuid().ToString("N"));
            string database = Path.Combine(directory, "test.kcal");
            Directory.CreateDirectory(directory);
            var store = new EventStore();
            try
            {
                store.Open(database);
                store.Close();
                store.OpenReadOnly(database);
                var view = new AppointmentViewStub();
                var editor = new AppointmentEditor(view, store);
                editor.NewAt(new DateTime(2026, 10, 20, 9, 0, 0));
                view.FieldTitle = "Unsaved";

                editor.Save();

                Assert.NotNull(view.Error);
                Assert.False(view.Closed);
                Assert.Null(view.Status);
                Assert.Empty(store.Events);
            }
            finally
            {
                store.Close();
                Directory.Delete(directory, true);
            }
        }

        private sealed class AppointmentViewStub : IAppointmentView
        {
            public Window Window => null!;
            public string? Error { get; private set; }
            public string? Status { get; private set; }
            public bool Closed { get; private set; }
            public string Loc(string key) => key;
            public void SetStatus(string text) => Status = text;
            public DraftChoice AskAboutDraft() => DraftChoice.Discard;
            public string FieldTitle { get; set; } = "";
            public string FieldLocation { get; set; } = "";
            public string FieldDescription { get; set; } = "";
            public string FieldAttendees { get; set; } = "";
            public string FieldCategories { get; set; } = "";
            public int? FieldReminder { get; set; }
            public bool FieldShowAsFree { get; set; }
            public void ShowMeeting(MeetingInfo? meeting) { }
            public string FieldZoneId { get; set; } = "";
            public void ShowZone(bool visible, bool editable) { }
            public string FieldStartDate { get; set; } = "";
            public string FieldStartTime { get; set; } = "";
            public string FieldEndDate { get; set; } = "";
            public string FieldEndTime { get; set; } = "";
            public RepeatFreq FieldRepeat { get; set; }
            public int FieldRepeatEveryN { get; set; } = 1;
            public int FieldRepeatMonthWeek { get; set; }
            public List<DateTime> FieldRepeatExtraDates { get; set; } = [];
            public List<DayOfWeek> FieldRepeatDays { get; set; } = [];
            public int FieldRepeatCount { get; set; }
            public DateTime? FieldRepeatUntil { get; set; }
            public void ResetRepeat() { FieldRepeat = RepeatFreq.None; }
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
}
