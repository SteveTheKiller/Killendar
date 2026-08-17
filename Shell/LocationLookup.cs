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

        private static HttpClient CreatePhotonClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Killendar/1.1 (https://killendar.net)");
            return client;
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
        }

        private void PhotonLookupToggle_Changed(object sender, RoutedEventArgs e)
        {
            _photonEnabled = PhotonLookupToggle.IsChecked == true;
            Settings.Set("PhotonLookup", _photonEnabled ? "1" : "0");
            if (_photonEnabled) return;
            _photonDelay.Stop();
            _photonCancel?.Cancel();
            LocationSuggestionsPopup.IsOpen = false;
        }

        private void FieldLocation_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_photonEnabled || _settingPhotonText) return;
            _photonDelay.Stop();
            _photonCancel?.Cancel();
            if (FieldLocation.Text.Trim().Length < 3)
            {
                LocationSuggestionsPopup.IsOpen = false;
                return;
            }
            _photonDelay.Start();
        }

        private async Task QueryPhotonAsync(string query)
        {
            if (!_photonEnabled || query.Length < 3) return;
            _photonCancel?.Cancel();
            var cancel = _photonCancel = new CancellationTokenSource();
            try
            {
                string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                string url = "https://photon.komoot.io/api/?limit=12&lang=" + Uri.EscapeDataString(lang)
                           + "&q=" + Uri.EscapeDataString(query);
                using var response = await PhotonClient.GetAsync(url, cancel.Token);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                if (cancel.IsCancellationRequested || !_photonEnabled || FieldLocation.Text.Trim() != query) return;
                ShowPhotonResults(ParsePhoton(json));
            }
            catch (OperationCanceledException) { }
            catch { LocationSuggestionsPopup.IsOpen = false; }
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
            if (results.Count == 0) return;
            LocationSuggestionsList.SelectedIndex = -1;
            LocationSuggestionsPopup.IsOpen = true;
        }

        private void LocationSuggestion_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (LocationSuggestionsList.SelectedItem is not string address) return;
            _settingPhotonText = true;
            FieldLocation.Text = address;
            FieldLocation.CaretIndex = address.Length;
            _settingPhotonText = false;
            LocationSuggestionsPopup.IsOpen = false;
            FieldLocation.Focus();
        }
    }
}
