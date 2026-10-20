using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Killendar.Features;
using Killendar.Services;
using Killendar.Shell;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class AgendaLanguageRegressionTests
    {
        [Fact]
        public void LanguagePickerPreservesAndTranslatesTheVisibleDayAgenda()
            => ScratchUiProcess.Run(typeof(AgendaLanguageFixture), "agenda");

        [Fact]
        public void LanguagePickerRefreshesEditorCaptionsWithoutLosingItsAgendaReturn()
            => ScratchUiProcess.Run(typeof(AgendaLanguageFixture), "editor");

        [Fact]
        public void LanguagePickerKeepsTheClosedSidebarClosed()
            => ScratchUiProcess.Run(typeof(AgendaLanguageFixture), "closed");
    }

    public sealed class AgendaLanguageFixture
    {
        public void Run(string scenario)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
                    Exercise(scenario);
                }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(20000)) throw new TimeoutException("The agenda language fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise(string scenario)
        {
            using var profile = new AgendaProfile();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Killendar;component/" + resource, UriKind.Relative)
                });
            ThemeManager.GetSetting = Settings.Get;
            ThemeManager.SetSetting = Settings.Set;
            var window = new MainWindow();
            try
            {
                var day = new DateTime(2026, 10, 20);
                if (scenario == "closed")
                {
                    var toggle = (Button)window.FindName("SidebarToggleBtn");
                    object? state = toggle.Tag;
                    SwitchLanguage(window, Locale.Fr);
                    Assert.Equal(state, toggle.Tag);
                    Assert.False(((FrameworkElement)window.FindName("SidebarContent")).IsEnabled);
                    Assert.Equal("0", Settings.Get("AppointmentSidebarOpen") ?? "0");
                    return;
                }
                ((ICalendarHost)window).ShowDayAgenda(day, null);
                var title = (TextBlock)window.FindName("SidebarTitle");
                if (scenario == "editor")
                {
                    ((Button)window.FindName("NewEventBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var editor = (IAppointmentView)window;
                    editor.FieldTitle = "Unsaved weekly appointment";
                    editor.FieldRepeat = Killendar.Models.RepeatFreq.Weekly;
                    editor.FieldRepeatDays = new List<DayOfWeek> { DayOfWeek.Tuesday };
                    editor.SeriesScopeVisible = true;
                    editor.EditWholeSeries = true;
                    string originalFrequency = (string)((Button)window.FindName("RepeatFreqButton")).Content;
                    string startDate = editor.FieldStartDate;
                    SwitchLanguage(window, Locale.Fr);
                    Assert.Equal(LocaleManager.Loc("Str_Side_New"), title.Text);
                    Assert.Equal(Visibility.Collapsed, ((ScrollViewer)window.FindName("AgendaScroll")).Visibility);
                    Assert.Equal("Unsaved weekly appointment", editor.FieldTitle);
                    Assert.Equal(startDate, editor.FieldStartDate);
                    Assert.NotEqual(originalFrequency, ((Button)window.FindName("RepeatFreqButton")).Content);
                    var days = ((WrapPanel)window.FindName("RepeatDaysRow")).Children.OfType<ToggleButton>().ToArray();
                    var format = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
                    Assert.Equal(7, days.Length);
                    for (int i = 0; i < days.Length; i++)
                        Assert.Equal(format.ShortestDayNames[((int)WeekStartManager.FirstDay + i) % 7], days[i].Content);
                    Assert.Equal(LocaleManager.Loc("Str_Rep_Weekly"), ((Button)window.FindName("RepeatFreqButton")).Content);
                    var ends = ((WrapPanel)window.FindName("RepeatEndRow")).Children.OfType<ToggleButton>().ToArray();
                    Assert.Equal(LocaleManager.Loc("Str_Rep_EndNever"), ends[0].Content);
                    var scope = ((WrapPanel)window.FindName("SeriesScopeChips")).Children.OfType<ToggleButton>().ToArray();
                    Assert.Equal(LocaleManager.Loc("Str_Btn_ThisDate"), scope[0].Content);
                    Assert.Equal(LocaleManager.Loc("Str_Btn_WholeSeries"), scope[1].Content);
                    Assert.True(editor.EditWholeSeries);
                    Assert.Equal(new[] { DayOfWeek.Tuesday }, editor.FieldRepeatDays);
                    editor.ClosePanel();
                    Assert.Equal(day.ToString("D"), title.Text);
                    Assert.Equal(Visibility.Visible, ((ScrollViewer)window.FindName("AgendaScroll")).Visibility);
                    return;
                }
                var rows = (StackPanel)window.FindName("AgendaListHost");
                string originalEmpty = Assert.Single(rows.Children.OfType<TextBlock>()).Text;
                SwitchLanguage(window, Locale.Fr);
                Assert.Equal(day.ToString("D"), title.Text);
                Assert.NotEqual(LocaleManager.Loc("Str_Side_New"), title.Text);
                Assert.Equal(Visibility.Visible, ((ScrollViewer)window.FindName("AgendaScroll")).Visibility);
                string translatedEmpty = Assert.Single(rows.Children.OfType<TextBlock>()).Text;
                Assert.Equal(LocaleManager.Loc("Str_Side_NoAppointments"), translatedEmpty);
                Assert.NotEqual(originalEmpty, translatedEmpty);
            }
            finally { app.Shutdown(); }
        }

        private static void SwitchLanguage(MainWindow window, Locale locale)
        {
            ((Button)window.FindName("LangButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = (ContextMenu)window.FindName("LangMenu");
            var columns = (StackPanel)((MenuItem)menu.Items[0]).Header;
            var radio = columns.Children.OfType<StackPanel>().SelectMany(column => column.Children.OfType<RadioButton>())
                .Single(button => Equals(button.Tag, locale.ToString()));
            radio.IsChecked = true;
            Assert.Equal(locale, LocaleManager.Current);
        }

        private sealed class AgendaProfile : IDisposable
        {
            private readonly string _root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\agenda-language", Guid.NewGuid().ToString("N"));
            private readonly FieldInfo _dir = typeof(Settings).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _path = typeof(Settings).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly FieldInfo _cache = typeof(Settings).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)!;
            private readonly object? _oldDir, _oldPath, _oldCache;
            public AgendaProfile()
            {
                Directory.CreateDirectory(_root);
                _oldDir = _dir.GetValue(null); _oldPath = _path.GetValue(null); _oldCache = _cache.GetValue(null);
                _dir.SetValue(null, _root);
                _path.SetValue(null, Path.Combine(_root, "settings.json"));
                _cache.SetValue(null, new Dictionary<string, string>
                {
                    ["KillendarDataDir"] = _root,
                    ["ActiveKillendar"] = "fixture.kcal",
                    ["CheckForUpdatesOnStartup"] = "0",
                    ["CloseToTray"] = "0"
                });
            }
            public void Dispose()
            {
                _dir.SetValue(null, _oldDir); _path.SetValue(null, _oldPath); _cache.SetValue(null, _oldCache);
                Directory.Delete(_root, true);
            }
        }
    }
}
