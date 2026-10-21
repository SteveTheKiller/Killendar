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
                Check1.Content = new System.Windows.Controls.TextBlock { Text = check1Label, TextWrapping = TextWrapping.Wrap };
                Check1.IsChecked = check1Initial;
                Check1.Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrEmpty(check2Label))
            {
                Check2.Content = new System.Windows.Controls.TextBlock { Text = check2Label, TextWrapping = TextWrapping.Wrap };
                Check2.IsChecked = check2Initial;
                Check2.Visibility = Visibility.Visible;
            }
        }

        // The family setup dialog's notice ring: an amber "!" on a confirmation and the accent "i" on a
        // notice with a single button. The amber darkens on a light card so it keeps its contrast.
        private void ApplyNotice()
        {
            FitHeading();
            FitCheckLabels();
            bool confirmation = CancelButton.Visibility == Visibility.Visible;
            System.Windows.Media.Brush brush;
            if (confirmation)
            {
                bool light = TryFindResource("BackgroundBrush") is System.Windows.Media.SolidColorBrush card && card.Color.R * 0.299 + card.Color.G * 0.587 + card.Color.B * 0.114 > 140;
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

        // Long checkbox labels wrap inside the dialog body instead of running past its edge.
        private void FitCheckLabels()
        {
            foreach (var box in new[] { Check1, Check2 })
                if (box.Content is System.Windows.Controls.TextBlock label && box.Parent is FrameworkElement body && body.ActualWidth > 0)
                    label.MaxWidth = System.Math.Max(80, body.ActualWidth - box.Margin.Left - box.Margin.Right - 40);
        }

        // A heading is one short line. Text after its first line break moves into the detail, and
        // a first line too long for a heading stays body-sized instead of shouting.
        private void FitHeading()
        {
            string text = HeadingText.Text ?? "";
            int lineBreak = text.IndexOf('\n');
            if (lineBreak >= 0)
            {
                string rest = text.Substring(lineBreak + 1).Trim();
                HeadingText.Text = text.Substring(0, lineBreak).TrimEnd('\r');
                if (rest.Length > 0)
                {
                    string existing = DetailText.Visibility == Visibility.Visible ? DetailText.Text : "";
                    DetailText.Text = string.IsNullOrEmpty(existing) ? rest : rest + "\n\n" + existing;
                    DetailText.Visibility = Visibility.Visible;
                }
            }
            if ((HeadingText.Text ?? "").Length > 90)
            {
                HeadingText.ClearValue(System.Windows.Controls.TextBlock.FontFamilyProperty);
                HeadingText.ClearValue(System.Windows.Media.TextOptions.TextFormattingModeProperty);
                HeadingText.FontSize = 13;
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

        /// <summary>True when the optional middle button closed the dialog. Confirmed is false then.</summary>
        public bool AltChosen { get; private set; }

        /// <summary>Shows a third, middle button. Esc and the close button still mean Cancel.</summary>
        public void ShowAlternative(string text)
        {
            AltButton.Content = text;
            AltButton.Visibility = Visibility.Visible;
        }

        private void Alt_Click(object sender, RoutedEventArgs e)
        {
            AltChosen = true;
            Confirmed = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}
