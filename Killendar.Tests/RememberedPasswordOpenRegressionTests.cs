using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Killendar.Controls;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killendar.Tests
{
    [Collection("Calendar settings")]
    public sealed class RememberedPasswordOpenRegressionTests
    {
        [Fact]
        public void RememberedPasswordInitializationFailureDoesNotPublishAnOpenCalendar()
            => ScratchUiProcess.Run(typeof(RememberedPasswordOpenFixture));
    }

    public sealed class RememberedPasswordOpenFixture
    {
        public void Run()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    System.IO.Packaging.PackUriHelper.Create(new Uri("file:///calendar.html"));
                    Exercise();
                }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(20000), "The scratch unlock fixture exceeded its deadline.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Exercise()
        {
            string root = Path.Combine(TestScratch.Root, "remembered-key-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var fields = new[] { "Dir", "FilePath", "_cache" }.Select(name =>
                typeof(Settings).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!).ToArray();
            object?[] previous = fields.Select(field => field.GetValue(null)).ToArray();
            var store = new EventStore();
            Application? app = null;
            try
            {
                fields[0].SetValue(null, root);
                fields[1].SetValue(null, Path.Combine(root, "settings.json"));
                fields[2].SetValue(null, new Dictionary<string, string> { ["KillendarDataDir"] = root });
                string good = Path.Combine(root, "good.kcal"), bad = Path.Combine(root, "bad.kcal");
                foreach (string path in new[] { good, bad })
                {
                    store.Open(path, "fixture key");
                    store.Add(new CalendarEvent { Title = "Saved appointment", Start = new DateTime(2026, 10, 20), End = new DateTime(2026, 10, 21) });
                    store.Close();
                }
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = bad, Password = "fixture key", Pooling = false }.ToString()))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DROP TABLE categories; CREATE TABLE categories (wrong TEXT)";
                    command.ExecuteNonQuery();
                }
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        { Source = new Uri("/Killendar;component/" + resource, UriKind.Relative) });
                var host = new Host();
                var security = new SecurityController(host, store);
                bool cancel = false;
                int prompts = 0;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += (_, _) =>
                {
                    foreach (var dialog in app.Windows.OfType<PasswordDialog>().ToArray())
                    {
                        prompts++;
                        ((PasswordBox)dialog.FindName("PwBox")).Password = "fixture key";
                        ((Button)dialog.FindName(cancel ? "CancelButton" : "OkButton"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                };
                timer.Start();
                try
                {
                    EventStore.SetActive(good);
                    Assert.True(security.Open(false));
                    Assert.Equal(1, prompts);
                    Assert.Equal("Saved appointment", Assert.Single(store.Events).Title);
                    cancel = true;
                    EventStore.SetActive(bad);
                    Assert.False(security.Open(false));
                    Assert.Equal(2, prompts);
                    Assert.False(store.IsOpen);
                    Assert.Empty(store.Events);
                    Assert.Equal("", store.FilePath);
                    using (File.Open(bad, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    EventStore.SetActive(good);
                    Assert.True(security.Open(false));
                    Assert.Equal(2, prompts);
                    Assert.Equal("Saved appointment", Assert.Single(store.Events).Title);
                }
                finally { timer.Stop(); }
            }
            finally
            {
                store.Close();
                app?.Shutdown();
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, previous[i]);
                Directory.Delete(root, true);
            }
        }

        private sealed class Host : ISecurityHost
        {
            public Window Window => null!;
            public string Loc(string key) => key + " {0}";
            public void SetStatus(string text) { }
            public void ShowLockState(bool encrypted) { }
            public void ShowLocked(bool locked) { }
            public void ShowActiveKillendar(string name, bool visible) { }
            public void RefreshView() { }
            public bool IsSidebarOpen => false;
            public void RestoreSidebar() { }
            public void CloseSidebar() { }
            public bool ConfirmLeaveDraft() => true;
        }
    }
}
