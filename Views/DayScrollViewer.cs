using System.Windows.Controls;
using System.Windows.Input;

namespace Killendar.Views
{
    /// <summary>
    /// A Month day's event list. It still takes keyboard focus on a click, but it does not mark
    /// the click handled the way a plain ScrollViewer does, so the click carries on to the day
    /// cell and opens that day. The list fills everything below the date number, and with a plain
    /// ScrollViewer only the strip holding the number opened the day. A chip still stops its own
    /// click before it gets here.
    /// </summary>
    internal sealed class DayScrollViewer : ScrollViewer
    {
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => Focus();
    }
}
