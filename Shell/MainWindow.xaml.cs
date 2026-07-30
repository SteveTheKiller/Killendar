using System.Windows;
using System.Windows.Input;
using Killendar.Features;

// The main window's constructor and the handlers that belong to no feature. The rest of MainWindow
// is split by surface: Chrome.cs (caption buttons, placement, grain), ThemeFlyout.cs (theme and
// accent swatches), About.cs (the About overlay), CalendarHost.cs (the calendar surface and the
// composition root), plus the host halves of the other features.
namespace Killendar.Shell
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            RestoreWindowPlacement();
            SourceInitialized += MainWindow_SourceInitialized;
            ApplyGrainTexture();
            Loaded += (_, _) =>
            {
                FadeInContent();
                // Deferred, NOT called inline: Loaded fires synchronously inside Show(), and the
                // unlock prompt both needs an already-shown Owner and may Close() the window on
                // cancel - a reentrant Close() during Show() throws. Dispatching lets Show()
                // finish first, so the prompt appears right after the first paint.
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    OpenCalendarData();        // opens, unlocks if needed, paints
                    HandlePendingOpenFile();   // a double-clicked .kcal, if any
                }), System.Windows.Threading.DispatcherPriority.Background);
            };

            InitThemePicker();
            InitLanguageMenu();

            _about = new AboutController(this);
            VersionLabel.Text = "v" + Services.AppInfo.Version;

            InitCalendar();   // store, the four views, navigation, ICS
        }

        /// <summary>The footer version label opens the About card.</summary>
        private void VersionLabel_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowAboutOverlay();
        }
    }
}
