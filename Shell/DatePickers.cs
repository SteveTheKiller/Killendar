using System.Windows;

namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void StartDatePicker_Click(object sender, RoutedEventArgs e)
            => Controls.DatePickerFlyout.Open(FieldStartDate, StartDatePickerBtn);

        private void EndDatePicker_Click(object sender, RoutedEventArgs e)
            => Controls.DatePickerFlyout.Open(FieldEndDate, EndDatePickerBtn);
    }
}
