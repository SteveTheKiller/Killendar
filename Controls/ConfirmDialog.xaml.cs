using System.Windows;
using System.Windows.Input;

// KillerUI kit. Replace "Killendar" with your app's root namespace.
namespace Killendar.Controls
{
    public partial class ConfirmDialog : Window
    {
        public bool Confirmed { get; private set; }

        /// <summary>State of the first optional checkbox. Meaningless unless check1Label was passed.</summary>
        public bool Check1Checked => Check1.IsChecked == true;

        /// <summary>State of the second optional checkbox. Meaningless unless check2Label was passed.</summary>
        public bool Check2Checked => Check2.IsChecked == true;

        public ConfirmDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
        }

        // Configurable variant. detail may contain newlines for multiple lines. check1Label /
        // check2Label opt in to up to two checkboxes, for a confirm that also carries a choice;
        // read them back via Check1Checked / Check2Checked after ShowDialog returns. The dialog
        // persists nothing itself.
        public ConfirmDialog(string heading, string detail, string confirmText, string cancelText = "Cancel",
                             string? check1Label = null, bool check1Initial = false,
                             string? check2Label = null, bool check2Initial = false)
            : this()
        {
            HeadingText.Text = heading;
            HeadingText.Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(detail) ? 0 : 12);
            DetailText.Text = detail;
            DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            OkButton.Content = confirmText;
            CancelButton.Content = cancelText;

            if (!string.IsNullOrEmpty(check1Label))
            {
                Check1.Content = check1Label;
                Check1.IsChecked = check1Initial;
                Check1.Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrEmpty(check2Label))
            {
                Check2.Content = check2Label;
                Check2.IsChecked = check2Initial;
                Check2.Visibility = Visibility.Visible;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}
