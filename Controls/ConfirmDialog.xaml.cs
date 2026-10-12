using System.Windows;
using System.Windows.Input;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;


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
            var titleFrames = BitmapDecoder.Create(new System.Uri("pack://application:,,,/Resources/kd-icon.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
            TitleIcon.Source = titleFrames[0];
            TaskbarIdentity.Track(this);
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            Loaded += (_, _) => ApplyNotice();
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
            DetailText.Text = detail;
            DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            OkButton.Content = confirmText;
            CancelButton.Content = cancelText;
            CancelButton.Visibility = string.IsNullOrEmpty(cancelText) ? Visibility.Collapsed : Visibility.Visible;

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

        // The family setup dialog's notice ring: an amber "!" on a confirmation and the accent "i" on a
        // notice with a single button. The amber darkens on a light card so it keeps its contrast.
        private void ApplyNotice()
        {
            bool confirmation = CancelButton.Visibility == Visibility.Visible;
            System.Windows.Media.Brush brush;
            if (confirmation)
            {
                var card = TryFindResource("BackgroundBrush") as System.Windows.Media.SolidColorBrush;
                bool light = card != null && card.Color.R * 0.299 + card.Color.G * 0.587 + card.Color.B * 0.114 > 140;
                brush = new System.Windows.Media.SolidColorBrush(light
                    ? System.Windows.Media.Color.FromRgb(176, 112, 0)
                    : System.Windows.Media.Color.FromRgb(255, 190, 80));
            }
            else
            {
                brush = TryFindResource("PrimaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
            }
            NoticeGlyph.Text = confirmation ? "!" : "i";
            NoticeGlyph.Foreground = brush;
            NoticeRing.BorderBrush = brush;
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
