using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Media;
using System.IO;
using Serilog;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Controls;          // DITAMBAHKAN: agar Canvas, ItemsControl dll dikenali
using System.Linq;                      // DITAMBAHKAN: untuk Where/OrderBy/FirstOrDefault
using System.Windows.Markup;
using System;

namespace DisplayApp.Wpf
{

    public partial class MainWindow : Window
    {
        private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:5000") };
        private HubConnection? _hub;
        private readonly DispatcherTimer _clockTimer = new();
        private readonly DispatcherTimer _marqueeTimer = new();
        private double _marqueeX = 0;
        private string _runningText = "Selamat datang di layanan antrian";
        private string? _videoPath;
        private string? _logoPath;
        private int? _activeCounter;

        private readonly ObservableCollection<string> _recentCalls = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                Focus();
                await InitAsync();
            };
        }

        private async Task InitAsync()
        {
            try
            {
                await LoadSettings();
                await LoadInitialActive();
                await InitHub();
                await RefreshServedLines();
                InitClock();
                InitMarquee();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InitAsync");
            }
        }

        private async Task LoadSettings()
        {
            try
            {
                var settings = await _http.GetFromJsonAsync<Dictionary<string, string>>("/api/settings") ?? new();

                settings.TryGetValue("RunningText", out _runningText);

                settings.TryGetValue("LogoPath", out _logoPath);
                settings.TryGetValue("VideoPath", out _videoPath);
                settings.TryGetValue("ShowLogo", out var showLogo);
                settings.TryGetValue("ShowVideo", out var showVideo);

                var logoImage = this.FindName("LogoImage") as System.Windows.Controls.Image;
                if (!string.IsNullOrWhiteSpace(_logoPath) && File.Exists(_logoPath) && showLogo == "true" && logoImage != null)
                {
                    try
                    {
                        var bi = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(_logoPath)));
                        logoImage.Source = bi;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Logo load fail");
                        logoImage.Source = null;
                    }
                }
                else if (logoImage != null)
                {
                    logoImage.Source = null;
                }

                var videoPlayer = this.FindName("VideoPlayer") as MediaElement;
                if (showVideo == "true")
                    LoadAndPlayVideo();
                else if (videoPlayer != null)
                    videoPlayer.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LoadSettings");
            }
        }

        private async Task LoadInitialActive()
        {
            try
            {
                var tickets = await _http.GetFromJsonAsync<List<TicketDto>>("/api/tickets/today") ?? new();
                NormalizeStatuses(tickets);

                var latestCalling = tickets
                    .Where(t => t.Status == "CALLING")
                    .OrderByDescending(t => t.CalledAt ?? DateTime.MinValue)
                    .FirstOrDefault();

                if (latestCalling != null)
                {
                    SetActive(latestCalling.TicketNumber, latestCalling.CounterNumber ?? 0);
                    AddRecentCall(latestCalling);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LoadInitialActive");
            }
        }

        private void NormalizeStatuses(List<TicketDto> tickets)
        {
            foreach (var t in tickets)
            {
                t.Status = t.Status switch
                {
                    "0" => "WAITING",
                    "1" => "CALLING",
                    "2" => "SERVING",
                    "3" => "DONE",
                    "4" => "NO_SHOW",
                    "5" => "CANCELED",
                    _ => t.Status
                };
            }
        }

        private async Task InitHub()
        {
            _hub = new HubConnectionBuilder()
                .WithUrl("http://localhost:5000/hub/queue")
                .WithAutomaticReconnect()
                .Build();

            _hub.On<object>("TicketCalled", payload =>
            {
                try
                {
                    string? ticketNumber = null;
                    int counter = 0;

                    if (payload is JsonElement je)
                    {
                        ticketNumber = je.GetPropertyOrNull("TicketNumber")?.GetString()
                            ?? je.GetPropertyOrNull("ticketNumber")?.GetString();
                        counter = je.GetPropertyOrNull("CounterNumber")?.GetInt32()
                            ?? je.GetPropertyOrNull("counterNumber")?.GetInt32()
                            ?? 0;
                    }
                    else
                    {
                        dynamic d = payload!;
                        ticketNumber = d?.TicketNumber ?? d?.ticketNumber;
                        counter = d?.CounterNumber ?? d?.counterNumber ?? 0;
                    }

                    if (!string.IsNullOrWhiteSpace(ticketNumber))
                    {
                        Dispatcher.Invoke(() => OnTicketCalled(ticketNumber, counter));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "TicketCalled handler");
                }
            });

            _hub.On<object>("SettingsChanged", _ =>
                Dispatcher.Invoke(async () => await LoadSettings()));

            _hub.On<object>("TicketUpdated", _ =>
                Dispatcher.Invoke(async () => await RefreshServedLines()));

            await _hub.StartAsync();
            Log.Information("Hub connected (Display)");
        }

        private void OnTicketCalled(string ticketNumber, int counter)
        {
            SetActive(ticketNumber, counter);

            AddRecentCall(new TicketDto
            {
                TicketNumber = ticketNumber,
                CounterNumber = counter
            });

            AnimateActiveFlash();
            _ = PlayChimeAndTts(ticketNumber, counter);
            _ = Dispatcher.InvokeAsync(async () => await RefreshServedLines());
        }

        private void AddRecentCall(TicketDto t)
        {
            var seq = TryExtractSequence(t.TicketNumber, out int s) ? s.ToString() : t.TicketNumber;
            var text = $"Nomor {seq} Loket {t.CounterNumber}";
            if (_recentCalls.FirstOrDefault() == text)
                return;

            _recentCalls.Insert(0, text);
            while (_recentCalls.Count > 3)
                _recentCalls.RemoveAt(_recentCalls.Count - 1);
        }

        private void SetActive(string ticketNumber, int counter)
        {
            var activeNumberText = this.FindName("ActiveNumberText") as TextBlock;
            var activeCounterText = this.FindName("ActiveCounterText") as TextBlock;
            if (activeNumberText != null)
            {
                activeNumberText.Text = TryExtractSequence(ticketNumber, out int seq)
                    ? seq.ToString()
                    : ticketNumber;
                // pastikan selalu merah
                var red = TryFindResource("ActiveNumberRed") as Brush ?? Brushes.Red;
                activeNumberText.Foreground = red;
            }
            _activeCounter = counter;
            if (activeCounterText != null)
                activeCounterText.Text = $"Ke Loket {counter}";
        }

        private bool TryExtractSequence(string ticketNumber, out int seq)
        {
            seq = 0;
            var parts = ticketNumber.Split('-', '_');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int v))
            {
                seq = v;
                return true;
            }
            var digits = new string(ticketNumber.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out v))
            {
                seq = v;
                return true;
            }
            return false;
        }

        private async Task RefreshServedLines()
        {
            try
            {
                var tickets = await _http.GetFromJsonAsync<List<TicketDto>>("/api/tickets/today") ?? new();
                NormalizeStatuses(tickets);

                var latestPerCounter = tickets
                    .Where(t => (t.Status == "CALLING" || t.Status == "SERVING") && t.CounterNumber != null)
                    .GroupBy(t => t.CounterNumber!.Value)
                    .Select(g => g.OrderByDescending(x => x.CalledAt ?? DateTime.MinValue).ThenByDescending(x => x.Id).First())
                    .Where(t => _activeCounter == null || t.CounterNumber != _activeCounter)
                    .OrderByDescending(t => t.CalledAt ?? DateTime.MinValue)
                    .ThenByDescending(t => t.Id)
                    .Take(3)
                    .ToList();

                var servedLine1 = this.FindName("ServedLine1") as TextBlock;
                var servedLine2 = this.FindName("ServedLine2") as TextBlock;
                var servedLine3 = this.FindName("ServedLine3") as TextBlock;
                var lines = new[] { servedLine1, servedLine2, servedLine3 };
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] == null) continue;
                    if (i < latestPerCounter.Count)
                    {
                        var t = latestPerCounter[i];
                        var seqText = TryExtractSequence(t.TicketNumber, out int s) ? s.ToString() : t.TicketNumber;
                        lines[i]!.Text = $"Nomor {seqText} Loket {t.CounterNumber}";
                    }
                    else
                    {
                        lines[i]!.Text = "-";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RefreshServedLines");
            }
        }

        private void AnimateActiveFlash()
        {
            var activeNumberText = this.FindName("ActiveNumberText") as TextBlock;
            if (activeNumberText == null) return;

            // animasi opacity (bukan warna)
            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.25,
                Duration = TimeSpan.FromMilliseconds(150),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(4),
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (_, __) =>
            {
                activeNumberText.Opacity = 1.0;
                var red = TryFindResource("ActiveNumberRed") as Brush ?? Brushes.Red;
                activeNumberText.Foreground = red;
            };

            activeNumberText.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void InitClock()
        {
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (_, _) =>
            {
                var culture = new System.Globalization.CultureInfo("id-ID");
                var clockText = this.FindName("ClockText") as TextBlock;
                var dateText = this.FindName("DateText") as TextBlock;
                if (clockText != null)
                    clockText.Text = DateTime.Now.ToString("HH:mm:ss");
                if (dateText != null)
                    dateText.Text = DateTime.Now.ToString("dddd, dd MMMM yyyy", culture);
            };
            _clockTimer.Start();
        }

        private void InitMarquee()
        {
            var runningTextBlock = this.FindName("RunningTextBlock") as TextBlock;
            if (runningTextBlock == null) return;
            runningTextBlock.Text = _runningText;
            _marqueeX = ActualWidth;
            _marqueeTimer.Interval = TimeSpan.FromMilliseconds(30);
            _marqueeTimer.Tick += (_, _) =>
            {
                _marqueeX -= 2.5;
                if (_marqueeX < -runningTextBlock.ActualWidth)
                    _marqueeX = ActualWidth;
                Canvas.SetLeft(runningTextBlock, _marqueeX);
                Canvas.SetTop(runningTextBlock, 8);
            };
            _marqueeTimer.Start();
        }

        private void LoadAndPlayVideo()
        {
            try
            {
                var videoPlayer = this.FindName("VideoPlayer") as MediaElement;
                if (videoPlayer != null && !string.IsNullOrWhiteSpace(_videoPath) && File.Exists(_videoPath))
                {
                    videoPlayer.Source = new Uri(Path.GetFullPath(_videoPath));
                    videoPlayer.MediaEnded += (_, _) =>
                    {
                        videoPlayer.Position = TimeSpan.Zero;
                        videoPlayer.Play();
                    };
                    videoPlayer.Play();
                }
                else
                {
                    Log.Warning("Video not found: {Path}", _videoPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LoadAndPlayVideo");
            }
        }

        private async Task PlayChimeAndTts(string ticketNumber, int counter)
        {
            await Task.Run(() =>
            {
                try
                {
                    var chimePath = "Resources\\chime.wav";
                    if (File.Exists(chimePath))
                    {
                        using var player = new System.Media.SoundPlayer(chimePath);
                        player.PlaySync();
                    }
                    using var synth = new System.Speech.Synthesis.SpeechSynthesizer();
                    synth.Rate = 0;

                    string verbal = ticketNumber;
                    if (TryExtractSequence(ticketNumber, out int seq))
                    {
                        var prefix = ticketNumber.Split('-', '_')[0];
                        verbal = $"{prefix} {seq}";
                    }
                    var kalimat = $"Nomor antrian {verbal}, silakan ke loket {counter}.";
                    synth.Speak(kalimat);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "PlayChimeAndTts");
                }
            });
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        }

        // Panggil ini setiap kali nomor aktif berubah
        private void BlinkActiveNumber()
        {
            if (ActiveNumberText == null) return;

            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.25,
                Duration = TimeSpan.FromMilliseconds(160),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3),
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (_, __) =>
            {
                ActiveNumberText.Opacity = 1; // pastikan kembali normal
                // jaga warna tetap merah
                var red = (Brush)FindResource("ActiveNumberRed");
                ActiveNumberText.Foreground = red;
            };

            ActiveNumberText.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // Contoh pemakaian setelah update nomor:
        private void SetActiveTicket(string numberText, string loketText)
        {
            ActiveNumberText.Text = numberText;
            ActiveCounterText.Text = $"Ke Loket {loketText}";
            BlinkActiveNumber();
        }
    }

    // DTO
    public class TicketDto
    {
        public int Id { get; set; }
        public string TicketNumber { get; set; } = "";
        public string Status { get; set; } = "";
        public int? CounterNumber { get; set; }
        public DateTime? CalledAt { get; set; }
    }

    // Extension helper untuk JsonElement
    internal static class JsonElementExt
    {
        public static JsonElement? GetPropertyOrNull(this JsonElement e, string name)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var val))
                return val;
            return null;
        }
    }
}