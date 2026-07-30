using System.Windows;
using System.Windows.Input;

// The Killendar - main window. This file holds only the constructor and the handlers that are
// not supplied by a kit partial. The rest of MainWindow lives in:
//   Chrome.cs       - caption buttons, maximize, corners, grain, window placement, resize grip
//   ThemeFlyout.cs  - theme + accent swatch flyout
//   About.cs        - the About overlay, signature check, update check, self-update
namespace Killendar
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            RestoreWindowPlacement();                           // Chrome.cs
            SourceInitialized += MainWindow_SourceInitialized;  // Chrome.cs
            ApplyGrainTexture();                                // Chrome.cs
            Loaded += (_, _) =>
            {
                FadeInContent();                                // Chrome.cs
                // Deferred, NOT called inline: Loaded fires synchronously inside Show(), and the
                // unlock prompt both needs an already-shown Owner and may Close() the window on
                // cancel - a reentrant Close() during Show() throws. Dispatching lets Show()
                // finish first, so the prompt appears right after the first paint.
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    OpenCalendarData();        // Calendar.cs - opens, unlocks if needed, paints
                    HandlePendingOpenFile();   // OpenFile.cs - a double-clicked .kcal, if any
                }), System.Windows.Threading.DispatcherPriority.Background);
            };

            UpdateThemeSwatchSelection();                       // ThemeFlyout.cs
            UpdateAccentSwatches();                             // ThemeFlyout.cs
            Services.ThemeManager.ThemeChanged += () =>
            {
                UpdateThemeSwatchSelection();
                UpdateAccentSwatches();
            };

            VersionLabel.Text = "v" + CurrentVersion;

            InitCalendar();   // Calendar.cs - store, the four views, navigation, ICS
        }

        /// <summary>Footer version label opens the About card, same as every other Killer app.</summary>
        private void VersionLabel_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowAboutOverlay();   // About.cs
        }
    }
}
