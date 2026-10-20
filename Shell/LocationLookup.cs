using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private static readonly HttpClient PhotonClient = CreatePhotonClient();
        private readonly DispatcherTimer _photonDelay = new() { Interval = TimeSpan.FromMilliseconds(650) };
        private CancellationTokenSource? _photonCancel;
        private bool _photonEnabled;
        private bool _settingPhotonText;
        internal Func<string, CancellationToken, Task<string>> PhotonRequestAsync { get; set; } = FetchPhotonAsync;

        private static HttpClient CreatePhotonClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Killendar/1.1 (https://killendar.net)");
            return client;
        }

        private static async Task<string> FetchPhotonAsync(string url, CancellationToken cancellation)
        {
            using var response = await PhotonClient.GetAsync(url, cancellation);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        private void InitLocationLookup()
        {
            _photonEnabled = Settings.Get("PhotonLookup") == "1";
            PhotonLookupToggle.IsChecked = _photonEnabled;
            _photonDelay.Tick += async (_, _) =>
            {
                _photonDelay.Stop();
                await QueryPhotonAsync(FieldLocation.Text.Trim());
            };
            Closed += (_, _) => CancelLocationLookup();
        }

        private void CancelLocationLookup()
        {
            _photonDelay.Stop();
            var cancellation = _photonCancel;
            _photonCancel = null;
            cancellation?.Cancel();
            LocationSuggestionsPopup.IsOpen = false;
            LocationSuggestionsList.Items.Clear();
        }

        private void PhotonLookupToggle_Changed(object sender, RoutedEventArgs e)
        {
            _photonEnabled = PhotonLookupToggle.IsChecked == true;
            Settings.Set("PhotonLookup", _photonEnabled ? "1" : "0");
            if (_photonEnabled) return;
            CancelLocationLookup();
        }

        private void FieldLocation_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settingPhotonText) return;
            CancelLocationLookup();
            if (!_photonEnabled || !_sidebarOpen || EditorScroll.Visibility != Visibility.Visible ||
                FieldLocation.Text.Trim().Length < 3) return;
            _photonDelay.Start();
        }

        private async Task QueryPhotonAsync(string query)
        {
            if (!_photonEnabled || !_sidebarOpen || EditorScroll.Visibility != Visibility.Visible || query.Length < 3) return;
            _photonCancel?.Cancel();
            var cancel = _photonCancel = new CancellationTokenSource();
            try
            {
                string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                string url = "https://photon.komoot.io/api/?limit=12&lang=" + Uri.EscapeDataString(lang)
                           + "&q=" + Uri.EscapeDataString(query);
                string json = await PhotonRequestAsync(url, cancel.Token);
                if (cancel.IsCancellationRequested || !ReferenceEquals(_photonCancel, cancel) || !_photonEnabled ||
                    !_sidebarOpen || EditorScroll.Visibility != Visibility.Visible || FieldLocation.Text.Trim() != query) return;
                ShowPhotonResults(ParsePhoton(json));
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (ReferenceEquals(_photonCancel, cancel) && !cancel.IsCancellationRequested)
                    CancelLocationLookup();
            }
            finally
            {
                if (ReferenceEquals(_photonCancel, cancel)) _photonCancel = null;
                cancel.Dispose();
            }
        }

        private static IReadOnlyList<string> ParsePhoton(string json)
        {
            var results = new List<string>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)) return results;
            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var p)) continue;
                string Get(string name) => p.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
                string streetName = Get("street");
                string street = string.Join(" ", new[] { Get("housenumber"), streetName }.Where(x => x.Length > 0));
                string place = Get("city"); if (place.Length == 0) place = Get("locality");
                string primary = street.Length > 0 ? street : Get("name");
                string value = string.Join(", ", new[] { primary, Get("postcode"), place, Get("state"), Get("country") }
                    .Where(x => x.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase));
                if (value.Length > 0 && !results.Contains(value)) results.Add(value);
            }
            return results;
        }

        private void ShowPhotonResults(IReadOnlyList<string> results)
        {
            LocationSuggestionsList.Items.Clear();
            foreach (string address in results)
                LocationSuggestionsList.Items.Add(address);
            if (results.Count == 0)
            {
                LocationSuggestionsPopup.IsOpen = false;
                return;
            }
            LocationSuggestionsList.SelectedIndex = -1;
            LocationSuggestionsPopup.IsOpen = true;
        }

        private void LocationSuggestion_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => AcceptLocationSuggestion();

        private void LocationSuggestion_PreviewKeyDown(object sender, KeyEventArgs e)
            => HandleLocationSuggestionKey(e);

        private bool HandleLocationSuggestionKey(KeyEventArgs e)
        {
            if (!LocationSuggestionsPopup.IsOpen) return false;
            bool inList = LocationSuggestionsList.IsKeyboardFocusWithin ||
                ReferenceEquals(e.OriginalSource, LocationSuggestionsList) ||
                (e.OriginalSource is DependencyObject origin && LocationSuggestionsList.IsAncestorOf(origin));
            if (!inList && !FieldLocation.IsKeyboardFocusWithin &&
                !ReferenceEquals(e.OriginalSource, FieldLocation)) return false;
            if (e.Key == Key.Escape || e.Key == Key.Tab)
            {
                CancelLocationLookup();
                if (e.Key == Key.Escape) FieldLocation.Focus();
                else if (inList)
                {
                    FieldLocation.Focus();
                    FieldLocation.MoveFocus(new TraversalRequest((Keyboard.Modifiers & ModifierKeys.Shift) != 0
                        ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
                }
                e.Handled = e.Key == Key.Escape || inList;
                return e.Handled;
            }
            if (Keyboard.Modifiers != ModifierKeys.None) return false;
            if ((e.Key == Key.Down || e.Key == Key.Up) && LocationSuggestionsList.Items.Count > 0)
            {
                int current = LocationSuggestionsList.SelectedIndex;
                int count = LocationSuggestionsList.Items.Count;
                LocationSuggestionsList.SelectedIndex = e.Key == Key.Down
                    ? Math.Min(count - 1, current + 1) : current < 0 ? count - 1 : Math.Max(0, current - 1);
                LocationSuggestionsList.ScrollIntoView(LocationSuggestionsList.SelectedItem);
                LocationSuggestionsList.Focus();
            }
            else if ((e.Key == Key.Enter || (e.Key == Key.Space && inList)) &&
                LocationSuggestionsList.SelectedItem is string) AcceptLocationSuggestion();
            else return false;
            e.Handled = true;
            return true;
        }

        private void AcceptLocationSuggestion()
        {
            if (LocationSuggestionsList.SelectedItem is not string address) return;
            CancelLocationLookup();
            _settingPhotonText = true;
            FieldLocation.Text = address;
            FieldLocation.CaretIndex = address.Length;
            _settingPhotonText = false;
            LocationSuggestionsPopup.IsOpen = false;
            FieldLocation.Focus();
        }
    }
}
