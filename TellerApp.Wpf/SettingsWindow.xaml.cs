using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using Microsoft.Win32;

namespace TellerApp.Wpf
{
    public partial class SettingsWindow : Window
    {
        private readonly HttpClient _http;

        public SettingsWindow(HttpClient http)
        {
            InitializeComponent();
            _http = http;
            Loaded += async (_, __) =>
            {
                try
                {
                    var s = await _http.GetFromJsonAsync<Dictionary<string, string>>("/api/settings") ?? new();
                    s.TryGetValue("RunningText", out var running);
                    s.TryGetValue("VideoPath", out var video);
                    s.TryGetValue("ShowVideo", out var showVideo);

                    RunningTextBox.Text = running ?? "";
                    VideoPathBox.Text = video ?? "";
                    ShowVideoCheck.IsChecked = string.Equals(showVideo, "true", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Gagal memuat settings.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Pilih Video",
                Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv|All Files|*.*"
            };
            if (dlg.ShowDialog(this) == true)
                VideoPathBox.Text = dlg.FileName;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var updates = new Dictionary<string, string>
                {
                    ["RunningText"] = RunningTextBox.Text ?? "",
                    ["VideoPath"] = VideoPathBox.Text ?? "",
                    ["ShowVideo"] = (ShowVideoCheck.IsChecked == true).ToString().ToLowerInvariant()
                };

                var resp = await _http.PutAsJsonAsync("/api/settings", updates);
                if (!resp.IsSuccessStatusCode)
                {
                    MessageBox.Show(this, $"Gagal menyimpan settings. Status: {resp.StatusCode}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Gagal menyimpan settings.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}