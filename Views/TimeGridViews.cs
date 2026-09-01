using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killendar.Models;
using Killendar.Services;

namespace Killendar.Views
{

    /// <summary>Seven days starting on the culture's first day of the week - or Monday to Friday
    /// when the work-week toggle is on (2026-07-31).</summary>
    internal sealed class WeekView : TimeGridView
    {
        public WeekView() : base(7) { }

        protected override int Days => CalendarChrome.WorkWeek ? 5 : 7;

        protected override bool OffersWorkWeekToggle => true;

        protected override DateTime FirstVisibleDay(DateTime anchor)
        {
            if (CalendarChrome.WorkWeek)
            {
                // Monday, whatever day the culture starts its week on - a work week is Mon to
                // Fri. A weekend anchor shows the week it belongs to (the Monday before it).
                int shiftM = ((int)anchor.DayOfWeek + 6) % 7;
                return anchor.Date.AddDays(-shiftM);
            }
            var first = WeekStartManager.FirstDay;
            int shift = ((int)anchor.DayOfWeek - (int)first + 7) % 7;
            return anchor.Date.AddDays(-shift);
        }

        public override string PeriodLabel
        {
            get
            {
                var s = RangeStart;
                var e = s.AddDays(Days - 1);
                return s.Year == e.Year && s.Month == e.Month
                    ? $"{s:MMM d} - {e.Day}, {e:yyyy}"
                    : $"{s:MMM d} - {e:MMM d}, {e:yyyy}";
            }
        }

        public override DateTime Step(DateTime from, int direction) => from.AddDays(7 * direction);
    }

    /// <summary>A single day.</summary>
    internal sealed class DayView : TimeGridView
    {
        public DayView() : base(1) { }

        protected override DateTime FirstVisibleDay(DateTime anchor) => anchor.Date;

        public override string PeriodLabel => Anchor.ToString("dddd, MMMM d, yyyy");

        public override DateTime Step(DateTime from, int direction) => from.AddDays(direction);
    }
}
