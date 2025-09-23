// filepath: d:\Kuliah\loket\QueueSystem\AdminApp.Wpf\MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using Microsoft.Win32;
using Shared;

namespace AdminApp.Wpf
{
    public partial class MainWindow : Window
    {
        private HttpClient _http = new();

        public MainWindow()
        {
            InitializeComponent();
            var url = ClientConfig.LoadServerUrl();
            if (string.IsNullOrWhiteSpace(url)) url = "http://localhost:5000";
            ServerUrlBox.Text = url;
            SetHttp(url);

            Loaded += async (_, __) => await LoadSettingsAsync(); // auto-muat saat dibuka
        }

        private void SetHttp(string url)
        {
            try { _http = new HttpClient { BaseAddress = new Uri(url) }; }
            catch { }
        }

        private async void ConnectAndSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = ServerUrlBox.Text.Trim().TrimEnd('/');
                SetHttp(url);
                var r = await _http.GetAsync("/api/debug/ping");
                if (!r.IsSuccessStatusCode) { Status("Gagal terhubung: " + r.StatusCode); return; }
                ClientConfig.SaveServerUrl(url);
                Status("Tersambung & URL disimpan");
            }
            catch (Exception ex) { Status("Error: " + ex.Message); }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Pilih Video", Filter = "Video|*.mp4;*.mkv;*.avi;*.mov|All|*.*" };
            if (dlg.ShowDialog(this) == true) VideoPathBox.Text = dlg.FileName;
        }

        private async void Upload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = VideoPathBox.Text;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Status("Pilih file video valid"); return; }

                using var form = new MultipartFormDataContent();
                await using var fs = File.OpenRead(path);
                form.Add(new StreamContent(fs), "file", System.IO.Path.GetFileName(path));
                var resp = await _http.PostAsync("/api/media/upload", form);
                if (!resp.IsSuccessStatusCode) { Status("Upload gagal: " + resp.StatusCode); return; }

                var res = await resp.Content.ReadFromJsonAsync<UploadResult>();
                if (res?.url != null)
                {
                    VideoPathBox.Text = res.url;          // URL server (relative /videos/xxx)
                    ShowVideoCheck.IsChecked = true;
                    Status("Upload berhasil. Display akan memutar video.");
                }
                else Status("Upload berhasil, tapi URL kosong.");
            }
            catch (Exception ex) { Status("Upload error: " + ex.Message); }
        }

        private async Task LoadSettingsAsync()
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
                Status("Settings dimuat");
            }
            catch (Exception ex) { Status("Gagal muat: " + ex.Message); }
        }

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
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
                Status(resp.IsSuccessStatusCode ? "Settings disimpan & dibroadcast" : $"Gagal simpan: {resp.StatusCode}");
            }
            catch (Exception ex) { Status("Error simpan: " + ex.Message); }
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, "Reset nomor antrian sekarang?", "Konfirmasi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                var resp = await _http.PostAsync("/api/admin/reset", null);
                Status(resp.IsSuccessStatusCode ? "Nomor di‑reset" : $"Gagal reset: {resp.StatusCode}");
            }
            catch (Exception ex) { Status("Error reset: " + ex.Message); }
        }

        private void Status(string msg) => StatusText.Text = msg;

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetHttp(ServerUrlBox.Text);
                var r = await _http.GetAsync("/api/debug/ping");
                if (r.IsSuccessStatusCode)
                {
                    MessageBox.Show("OK terhubung");
                }
                else
                {
                    var body = await r.Content.ReadAsStringAsync();
                    MessageBox.Show($"Gagal: {r.StatusCode}\n{body}");
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
        }

        private sealed class UploadResult { public string? url { get; set; } }
    }
}