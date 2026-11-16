using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Killendar.Services;

namespace Killendar.Controls
{
    /// <summary>Reads a typed line into a new appointment. Closes with Result set on Enter; the
    /// shell opens the edit panel with it, and nothing is saved here.</summary>
    public partial class QuickAddDialog : Window
    {
        internal QuickAddResult? Result { get; private set; }

        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        public QuickAddDialog()
        {
            InitializeComponent();
            TitleIcon.Source = BitmapDecoder.Create(new Uri("pack://application:,,,/Resources/kd-icon.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            TaskbarIdentity.Track(this);
            Loaded += (_, _) =>
            {
                Anim.FadeIn(RootBorder);
                LineBox.Focus();
            };
        }

        private void LineBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (PreviewText == null) return;   // TextChanged can fire during InitializeComponent
            PreviewText.Text = LineBox.Text.Trim().Length == 0
                ? ""
                : Describe(Shell.MainWindow.ReadQuickAdd(LineBox.Text));
        }

        /// <summary>One line saying how the text was read: title, when, and where.</summary>
        internal static string Describe(QuickAddResult r)
        {
            string title = r.Title.Length == 0 ? Loc("Str_Cal_NoTitle") : r.Title;
            string when;
            if (r.AllDay)
                when = DateFormatManager.Format(r.Start) + ", " + Loc("Str_QA_AllDay");
            else if (r.End.Date == r.Start.Date)
                when = DateFormatManager.Format(r.Start) + " " + DateFormatManager.Time(r.Start) + " - " + DateFormatManager.Time(r.End);
            else
                when = DateFormatManager.Format(r.Start) + " " + DateFormatManager.Time(r.Start) + " - "
                     + DateFormatManager.Format(r.End) + " " + DateFormatManager.Time(r.End);
            string text = title + "    " + when;
            if (r.Location.Length > 0) text += "    " + string.Format(Loc("Str_QA_Where"), r.Location);
            if (r.Categories.Length > 0) text += "    #" + r.Categories.Replace(", ", " #");
            return text;
        }

        private void LineBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (LineBox.Text.Trim().Length > 0)
                    {
                        Result = Shell.MainWindow.ReadQuickAdd(LineBox.Text);
                        Close();
                    }
                    e.Handled = true;
                    break;
                case Key.Escape: Close(); e.Handled = true; break;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            TitleBarDrag.Start(this, e);
    }
}
