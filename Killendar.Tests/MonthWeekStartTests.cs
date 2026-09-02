using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using Killendar.Services;
using Killendar.Views;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class MonthWeekStartTests
    {
        [Fact]
        public void RefreshRealignsVisibleDatesWithoutNavigation()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                var previous = WeekStartManager.Current;
                var save = WeekStartManager.SetSetting;
                try
                {
                    if (System.Windows.Application.Current == null) new System.Windows.Application();
                    WeekStartManager.SetSetting = (_, _) => { };
                    foreach (int weeks in new[] { 0, 4 })
                    foreach (var before in new[] { WeekStartStyle.Sunday, WeekStartStyle.Monday })
                    {
                        var after = before == WeekStartStyle.Sunday ? WeekStartStyle.Monday : WeekStartStyle.Sunday;
                        WeekStartManager.Apply(before);
                        var view = new MonthView();
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        typeof(MonthView).GetField("_visibleWeeks", flags).SetValue(view, weeks);
                        view.Anchor = new DateTime(2026, 9, 2);
                        view.Initialize(new EventStore());
                        var cells = (IDictionary)typeof(MonthView).GetField("_cells", flags).GetValue(view);
                        var original = cells.Keys.Cast<DateTime>().OrderBy(d => d).ToArray();
                        WeekStartManager.Apply(after);
                        view.Refresh();
                        var first = cells.Keys.Cast<DateTime>().Min();
                        Assert.Equal(after == WeekStartStyle.Monday ? DayOfWeek.Monday : DayOfWeek.Sunday, first.DayOfWeek);
                        var snapshot = cells.Keys.Cast<DateTime>().OrderBy(d => d).ToArray();
                        view.Refresh();
                        Assert.Equal(snapshot, cells.Keys.Cast<DateTime>().OrderBy(d => d).ToArray());
                        WeekStartManager.Apply(before);
                        view.Refresh();
                        Assert.Equal(original, cells.Keys.Cast<DateTime>().OrderBy(d => d).ToArray());
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    WeekStartManager.Apply(previous);
                    WeekStartManager.SetSetting = save;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
