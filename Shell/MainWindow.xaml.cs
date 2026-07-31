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
                RefreshPortableBadge();   // Install.cs - footer badge when not running installed
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

            // Every flyout opens at the content pane's bottom-left corner: inside the window, above
            // the footer, clear of the rail. Set before the flyouts are built (FlyoutPlacement.cs).
            Controls.FlyoutPlacement.UsePane(ContentPane);

            InitThemePicker();
            InitLanguageMenu();
            // After InitLanguageMenu: the toolbar captions are read through Loc(), so the locale
            // has to be settled before they are built. (ToolbarStyle.cs)
            InitToolbarStyle();
            // After InitToolbarStyle: the overflow measurement depends on the button widths that
            // the display mode sets. (ToolbarOverflow.cs)
            InitToolbarOverflow();

            // Before InitCalendar: the views read CalendarChrome.HourHeight while they build, so the
            // saved density has to be in place or the first paint uses the default and then jumps.
            InitDensity();

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
