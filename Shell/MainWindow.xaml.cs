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
        /// <summary>Close to system tray (Shell/CloseToTray.cs): hides the window to the notification
        /// area from the first line of OnClosing when the About option is on.</summary>
        private readonly KillerFamily.Shell.CloseToTray _tray;

        /// <summary>A second launch brings the window back, from the notification area too.</summary>
        internal void ShowFromTray() => _tray.ShowWindow();

        public MainWindow()
        {
            InitializeComponent();
            Controls.SidebarEdgeFade.Attach(EditorScroll);
            Controls.SidebarEdgeFade.Attach(AgendaScroll);
            _tray = KillerFamily.Shell.CloseToTray.Attach(this, Services.AppInfo.DisplayName, Loc, () => AboutController.CloseToTrayEnabled);
            new KillerFamily.Accessibility.OverlayFocus(this).Track(ShortcutsOverlay, AboutOverlay);
            var titleFrames = System.Windows.Media.Imaging.BitmapDecoder.Create(
                new System.Uri("pack://application:,,,/Resources/kd-icon.ico"),
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames;
            foreach (var icon in new[] { WordmarkTitleIcon, PlainTitleIcon })
            {
                icon.Source = titleFrames[0];
            }
            InitSidebarResize();
            InitSidebarSide();   // SidebarDock.cs - left or right of the calendar
            InitRailOrder();

            RestoreWindowPlacement();
            InitAppScale();
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
                    HandlePendingOpenFile();   // a shell-opened .kcal or .ics, if any
                    if (string.Equals(Settings.Get("AppointmentSidebarOpen"), "1",
                                      System.StringComparison.Ordinal))
                        RestoreSidebarPanel();
                    // After the unlock prompt, so an update offer never stacks on it (About.cs).
                    _about.CheckOnStartup();
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
            InitWorkWeek();
            InitLocationLookup();

            _about = new AboutController(this);
            VersionLabel.Text = "v" + Services.AppInfo.Version;

            InitCalendar();   // store, the four views, navigation, ICS
            ApplyShortcutTooltips();   // ShortcutsOverlay.cs - the keys on every bound button
        }

        /// <summary>The family rail order (RailOrder.cs): theme, language and shortcuts hold the
        /// rail's corner, and the other rail buttons can be dragged into any order.</summary>
        private void InitRailOrder()
        {
            RailOrder.Attach(RailStack,
                new FrameworkElement[] { ThemeButton, LangButton, ShortcutsButton },
                new FrameworkElement[] { CategoriesButton, DensityBtn, DateSettingsButton },
                Settings.Get("RailOrder"), value => Settings.Set("RailOrder", value));
        }

        /// <summary>The footer version label opens the About card.</summary>
        private void VersionLabel_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowAboutOverlay();
        }
    }
}
