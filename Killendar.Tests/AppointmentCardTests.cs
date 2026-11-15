using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Killendar.Models;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>An appointment's right-click menu is a family card: the appointment's own title on
    /// top, a strip of Open, Edit, Duplicate and Delete, then the rows. Another Killendar's
    /// appointment is read only, so its strip offers Open alone.</summary>
    [Collection("Calendar settings")]
    public sealed class AppointmentCardTests
    {
        private static CalendarEvent Appointment(string source = "", Guid? series = null) => new()
        {
            Title = "Dentist",
            Location = "Main St",
            Start = new DateTime(2026, 11, 16, 9, 0, 0),
            End = new DateTime(2026, 11, 16, 10, 0, 0),
            SourceCalendar = source,
            SourceColor = source.Length > 0 ? "#3366CC" : "",
            SeriesId = series,
        };

        [Fact]
        public void TheCardIsNamedForTheAppointmentAndOffersEveryAction() => OnSta(() =>
        {
            var card = CalendarChrome.AppointmentCard(Appointment(), other: false, _ => { });

            Assert.Equal("Dentist", AutomationProperties.GetName((MenuItem)card.Items[0]));
            Assert.Equal(["Enter", "", "Ctrl+D", "Ctrl+Delete"],
                StripButtons(card).Select(b => AutomationProperties.GetAcceleratorKey(b)).ToArray());
            Assert.Contains("(Ctrl+D)", (string)StripButtons(card)[2].ToolTip);
        });

        [Fact]
        public void AnotherKillendarsAppointmentOffersOpenAlone() => OnSta(() =>
        {
            var card = CalendarChrome.AppointmentCard(Appointment("Work"), other: true, _ => { });
            Assert.Single(StripButtons(card));
        });

        [Fact]
        public void OneDateOfASeriesAlsoOffersTheSeries() => OnSta(() =>
        {
            var plain = CalendarChrome.AppointmentCard(Appointment(), other: false, _ => { });
            var dated = CalendarChrome.AppointmentCard(Appointment(series: Guid.NewGuid()), other: false, _ => { });
            Assert.Equal(2, plain.Items.Count);
            Assert.Equal(3, dated.Items.Count);
        });

        [Fact]
        public void TheStripRunsTheActionForThisAppointment() => OnSta(() =>
        {
            var ev = Appointment();
            CalendarEvent? opened = null, deleted = null, copied = null;
            var keepDelete = CalendarChrome.DeleteRequested;
            var keepDuplicate = CalendarChrome.DuplicateRequested;
            try
            {
                CalendarChrome.DeleteRequested = e => deleted = e;
                CalendarChrome.DuplicateRequested = e => copied = e;
                var buttons = StripButtons(CalendarChrome.AppointmentCard(ev, other: false, e => opened = e));
                foreach (var b in buttons) b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            finally
            {
                CalendarChrome.DeleteRequested = keepDelete;
                CalendarChrome.DuplicateRequested = keepDuplicate;
            }
            Assert.Same(ev, opened);
            Assert.Same(ev, copied);
            Assert.Same(ev, deleted);
        });

        [Fact]
        public void RowsAddedUnderTheStripDrawNoSecondRule() => OnSta(() =>
        {
            var card = CalendarChrome.AppointmentCard(Appointment(), other: false, _ => { });
            CalendarChrome.AddRowGroup(card);
            Assert.IsNotType<Separator>(card.Items[card.Items.Count - 1]);

            var series = CalendarChrome.AppointmentCard(Appointment(series: Guid.NewGuid()), other: false, _ => { });
            CalendarChrome.AddRowGroup(series);
            Assert.IsType<Separator>(series.Items[series.Items.Count - 1]);
        });

        private static Button[] StripButtons(ContextMenu card) =>
            ((UniformGrid)((Border)((MenuItem)card.Items[1]).Header).Child).Children.OfType<Button>().ToArray();

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    TestApplication.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The appointment card test hung.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }
    }
}
