using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;                        // FIX: TryExtractSequence helper
using System.IO;                          // FIX: Path, File, Directory
using System.Net.Http;                    // FIX: HttpClient
using System.Net.Http.Json;
using System.Text.Json;                   // FIX: JsonElement (jika terpakai)
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;               // Brush
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;       // BitmapImage, BitmapCacheOption
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;                            // FIX: Log
using Shared;                             // FIX: ClientConfig
using Shared.Contracts;
// alias MediaPlayer untuk hindari konflik nama
using WpfMediaPlayer = System.Windows.Media.MediaPlayer;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace DisplayApp.Wpf
{
    public partial class MainWindow : Window
    {
        // ==== Re-added missing backing fields ====
        // Settings / polling
        private readonly DispatcherTimer _settingsPollTimer = new() { Interval = TimeSpan.FromSeconds(10) };
        private string? _settingsSignature;

        // SignalR hub
        private HubConnection? _hub;

        // Recent calls (for display list)
        private readonly ObservableCollection<TicketDto> _recentCalls = new();
        private const int MaxRecent = 10;
        private int _activeCounter = 0;

        // Clock
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        // Marquee (running text)
        private readonly DispatcherTimer _marqueeTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        private double _marqueeTextWidth;
        private double _marqueeX;
        private const double MarqueeSpeed = 2.0; // px per tick

        // Video / VLC
        private string? _videoPath;
        private LibVLC? _libvlc;
        private VlcMediaPlayer? _vlcPlayer;

        // Logos rotation
        private readonly DispatcherTimer _logoTimer = new() { Interval = TimeSpan.FromSeconds(6) };
        private readonly List<string> _logoFiles = new();
        private bool _logoRotationStarted = false;
        private int _logoIndex = -1;
        private bool _logoUseA = true;

        // Audio (if not already present elsewhere)
        private static string? FindAudio(string fileName)
        {
            var roots = new[]
            {
                AppContext.BaseDirectory,
                Path.Combine(AppContext.BaseDirectory, "audio"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..","audio")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..","..","audio")),
            };
            foreach (var r in roots)
            {
                var p = Path.Combine(r, fileName);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // (If constructor lost timer wiring, ensure this runs)
        private void InitTimers()
        {
            _settingsPollTimer.Tick += async (_, _) => await LoadSettings();
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            _marqueeTimer.Tick += MarqueeTimer_Tick;
            _logoTimer.Tick += LogoTimer_Tick;
        }

        // Call this early in ctor after InitializeComponent()
        private void EnsureInitOnce()
        {
            if (!_clockTimer.IsEnabled)
            {
                InitTimers();
                _clockTimer.Start();
            }
        }

        // --- Audio: single player serial ---
        private readonly SemaphoreSlim _audioLock = new(1, 1);
        private readonly Queue<string> _audioQueue = new();
        private WpfMediaPlayer? _voicePlayer;
        private bool _sequenceActive = false;
        private bool _releaseLockWhenEmpty = false;
        private const int GapMs = 70;

        // Server & HTTP
        private static readonly string ServerUrl = ClientConfig.LoadServerUrl();
        private readonly HttpClient _http;
        public MainWindow()
        {
            try
            {
                _http = new HttpClient { BaseAddress = new Uri(ServerUrl) };
            }
            catch
            {
                // fallback aman
                _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
            }
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
                await LoadSettings();            // ambil awal
                await LoadInitialActive();
                await InitHub();                 // hub connect
                await RefreshServedLines();
                InitClock();
                InitMarquee();

                // Start polling ringan sebagai fallback jika event terlewat
                _settingsPollTimer.Interval = TimeSpan.FromSeconds(5);
                _settingsPollTimer.Tick += async (_, __) =>
                {
                    try { await LoadSettings(); } catch (Exception ex) { Log.Error(ex, "SettingsPoll"); }
                };
                _settingsPollTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InitAsync");
            }
        }

        private async Task InitHub()
        {
            _hub = new HubConnectionBuilder()
                .WithUrl($"{ServerUrl}/hub/queue")
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

        // Ganti ke konstanta & tambahkan log
        _hub.On<object>(HubEvents.SettingsChanged, _ =>
        {
            Log.Information("SettingsChanged event diterima");
            _ = Dispatcher.InvokeAsync(async () => await LoadSettings());
        });

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
                Number = ticketNumber,
                Counter = counter,
                CalledAt = DateTime.Now
            });

            AnimateActiveFlash();
            _ = PlayChimeAndTts(ticketNumber, counter);
            _ = Dispatcher.InvokeAsync(async () => await RefreshServedLines());
        }

        private void AddRecentCall(string number, int counter)
        {
            if (string.IsNullOrWhiteSpace(number)) return;
            if (_recentCalls.Count > 0 && _recentCalls[0].Number == number) return;
            _recentCalls.Insert(0, new TicketDto { Number = number, Counter = counter, Time = DateTime.Now });
            while (_recentCalls.Count > MaxRecent)
                _recentCalls.RemoveAt(_recentCalls.Count - 1);
        }
        private void AddRecentCall(TicketDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Number)) return;
            if (_recentCalls.Count > 0 && _recentCalls[0].Number == dto.Number) return;
            _recentCalls.Insert(0, dto);
            while (_recentCalls.Count > MaxRecent)
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

        private static bool TryExtractSequence(string input, out int seq)
        {
            var digits = new string(input.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out seq);
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
                    .Select(g => g
                        .OrderByDescending(x => x.CalledAt)
                        .ThenByDescending(x => x.Id).First())
                    .Where(t => t.CounterNumber != _activeCounter)          // _activeCounter int (non-nullable)
                    .OrderByDescending(t => t.CalledAt)                     // CalledAt sudah non-nullable
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
            var text = this.FindName("RunningTextBlock") as TextBlock;
            var canvas = this.FindName("RunningCanvas") as Canvas;
            if (text == null || canvas == null) return;

            // hitung lebar teks
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _marqueeTextWidth = text.DesiredSize.Width;

            // mulai dari sisi kanan canvas
            _marqueeX = canvas.ActualWidth <= 0 ? ActualWidth : canvas.ActualWidth;
            Canvas.SetTop(text, 8);
            Canvas.SetLeft(text, _marqueeX);

            _marqueeTimer.Stop();
            _marqueeTimer.Tick -= MarqueeTimer_Tick;
            _marqueeTimer.Interval = TimeSpan.FromMilliseconds(30);
            _marqueeTimer.Tick += MarqueeTimer_Tick;
            _marqueeTimer.Start();

            SizeChanged -= MainWindow_SizeChanged;
            SizeChanged += MainWindow_SizeChanged;
        }

        private void MarqueeTimer_Tick(object? sender, EventArgs e)
        {
            if (this.FindName("RunningTextBlock") is not TextBlock text) return;
            if (this.FindName("RunningCanvas") is not Canvas canvas) return;

            var width = canvas.ActualWidth > 0 ? canvas.ActualWidth : ActualWidth;

            _marqueeX -= MarqueeSpeed;
            Canvas.SetLeft(text, _marqueeX);

            // reset setelah seluruh teks keluar kiri
            if (_marqueeX < -_marqueeTextWidth)
            {
                _marqueeX = width;
                Canvas.SetLeft(text, _marqueeX);
            }
        }

        private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (this.FindName("RunningCanvas") is not Canvas canvas) return;
            _marqueeX = canvas.ActualWidth > 0 ? canvas.ActualWidth : ActualWidth;
        }

        private void ApplyRunningText(string? textValue)
        {
            if (this.FindName("RunningTextBlock") is not TextBlock text) return;
            if (this.FindName("RunningCanvas") is not Canvas canvas) return;

            text.Text = textValue ?? string.Empty;
            text.TextWrapping = TextWrapping.NoWrap;

            // hitung ulang lebar teks
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _marqueeTextWidth = text.DesiredSize.Width;

            // mulai dari kanan canvas
            _marqueeX = canvas.ActualWidth > 0 ? canvas.ActualWidth : ActualWidth;
            Canvas.SetTop(text, 8);
            Canvas.SetLeft(text, _marqueeX);

            // siapkan timer sekali
            _marqueeTimer.Stop();
            _marqueeTimer.Tick -= MarqueeTimer_Tick;
            _marqueeTimer.Interval = TimeSpan.FromMilliseconds(30);
            _marqueeTimer.Tick += MarqueeTimer_Tick;
            _marqueeTimer.Start();

            SizeChanged -= MainWindow_SizeChanged;
            SizeChanged += MainWindow_SizeChanged;
        }

        // Hitung signature untuk deteksi perubahan setting
        private static string ComputeSignature(IDictionary<string, string> s)
        {
            s.TryGetValue("RunningText", out var running);
            s.TryGetValue("VideoPath", out var video);
            s.TryGetValue("ShowVideo", out var showVideo);
            return $"{running}|{video}|{showVideo}";
        }

        private string? _lastRunTextSig;
        private bool? _lastShowLogo;

        private async Task LoadSettings()
        {
            try
            {
                var settings = await _http.GetFromJsonAsync<Dictionary<string, string>>("/api/settings") ?? new();

                var firstLoad = _settingsSignature == null;
                var newSig = ComputeSignature(settings); // berisi RunningText|VideoPath|ShowVideo
                var changed = firstLoad || !string.Equals(_settingsSignature, newSig, StringComparison.Ordinal);
                _settingsSignature = newSig;

                settings.TryGetValue("RunningText", out var runningText);
                settings.TryGetValue("VideoPath", out _videoPath);
                settings.TryGetValue("ShowVideo", out var showVideo);
                settings.TryGetValue("ShowLogo", out var showLogoStr);

                // 1) Running text: hanya re-init saat teks berubah
                var rtSig = runningText ?? string.Empty;
                if (firstLoad || _lastRunTextSig != rtSig)
                {
                    _lastRunTextSig = rtSig;
                    ApplyRunningText(runningText);
                }

                // 2) Logo: hanya start/stop saat toggle berubah
                var showLogo = string.IsNullOrWhiteSpace(showLogoStr) || showLogoStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (firstLoad || _lastShowLogo != showLogo)
                {
                    _lastShowLogo = showLogo;
                    StartLogoRotation(showLogo);
                }

                // 3) Video: apply saat pertama atau ada perubahan terkait video
                if (firstLoad || changed)
                {
                    var videoEl = this.FindName("VideoPlayer") as MediaElement;
                    if (string.Equals(showVideo, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadAndPlayVideo();
                    }
                    else if (videoEl != null)
                    {
                        videoEl.Visibility = Visibility.Collapsed;
                        videoEl.Stop();
                        videoEl.Source = null;
                    }
                }

                Log.Information("Settings loaded (changed={Changed})", changed);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LoadSettings");
            }
        }

        private async Task LoadInitialActive()
        {
            // Jika belum ada endpoint khusus, biarkan no-op agar tidak error kompilasi.
            await Task.CompletedTask;
        }

        private void NormalizeStatuses() { /* no-op */ }

        // Overload generik agar kompatibel dengan pemanggilan yang ada
        private void NormalizeStatuses<T>(T _) { /* no-op for now */ }

        // --- Video helpers ---

        private static string? ResolveMediaPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            // HTTP/HTTPS absolut
            if (Uri.TryCreate(path, UriKind.Absolute, out var abs) &&
                (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
                return abs.ToString();

            // Relatif ke server (contoh: /videos/xxx.mp4)
            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                var baseUri = new Uri(ClientConfig.LoadServerUrl().TrimEnd('/'));
                return new Uri(baseUri, path).ToString();
            }

            // Path lokal absolut/UNC
            if (Path.IsPathRooted(path) && File.Exists(path))
                return Path.GetFullPath(path);

            // Relatif ke folder app
            var p1 = Path.Combine(AppContext.BaseDirectory, path);
            if (File.Exists(p1)) return Path.GetFullPath(p1);

            // Relatif ke Resources lokal
            var p2 = Path.Combine(AppContext.BaseDirectory, "Resources", Path.GetFileName(path));
            if (File.Exists(p2)) return Path.GetFullPath(p2);

            return null;
        }

        private void LoadAndPlayVideo()
        {
            try
            {
                if (this.FindName("VideoPlayer") is not MediaElement video) return;
                var vlcView = this.FindName("VlcView") as LibVLCSharp.WPF.VideoView;

                var resolved = ResolveMediaPath(_videoPath);
                if (resolved == null)
                {
                    Log.Warning("Video not found: {Path}", _videoPath);
                    video.Visibility = Visibility.Collapsed;
                    if (vlcView != null) vlcView.Visibility = Visibility.Collapsed;
                    return;
                }

                var ext = Path.GetExtension(resolved).ToLowerInvariant();

                // VLC fallback untuk format non-standar
                if (ext is ".mkv" or ".webm" or ".mk3d" or ".mov")
                {
                    _libvlc ??= new LibVLC(enableDebugLogs: false);
                    _vlcPlayer?.Stop();
                    _vlcPlayer?.Dispose();
                    _vlcPlayer = new VlcMediaPlayer(_libvlc) { EnableHardwareDecoding = true };
                    if (vlcView != null) vlcView.MediaPlayer = _vlcPlayer;

                    video.Visibility = Visibility.Collapsed;
                    if (vlcView != null) vlcView.Visibility = Visibility.Visible;

                    using var media = new Media(_libvlc, resolved, FromType.FromLocation);
                    media.AddOption(":input-repeat=65535"); // loop
                    _vlcPlayer.Play(media);
                    Log.Information("VLC playing: {Url}", resolved);
                    return;
                }

                // Default: MediaElement (mp4, wmv)
                if (vlcView != null)
                {
                    vlcView.Visibility = Visibility.Collapsed;
                    _vlcPlayer?.Stop();
                }

                video.MediaOpened -= Video_MediaOpened;
                video.MediaEnded  -= Video_MediaEnded;
                video.MediaFailed -= Video_MediaFailed;
                video.MediaOpened += Video_MediaOpened;
                video.MediaEnded  += Video_MediaEnded;
                video.MediaFailed += Video_MediaFailed;

                video.Visibility = Visibility.Visible;
                video.IsMuted = true;
                video.Stop();
                video.Source = new Uri(resolved, UriKind.RelativeOrAbsolute);
                Log.Information("MediaElement playing: {Url}", resolved);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LoadAndPlayVideo failed");
            }
        }

        // OPTIONAL: jika sudah ada versi lain, ganti dengan yang ini
        private IEnumerable<string> BuildNumberAudio(int number)
        {
            if (number >= 0 && number <= 11)
            {
                var direct = $"{number}.mp3";
                if (FindAudio(direct) != null)
                {
                    yield return direct;
                    yield break;
                }
            }
            if (number >= 12 && number <= 19)
            {
                yield return $"{number - 10}.mp3";
                yield return "belas.mp3";
                yield break;
            }
            if (number >= 20 && number < 100)
            {
                var tens = number / 10;
                var ones = number % 10;
                yield return $"{tens}.mp3";
                yield return "puluh.mp3";
                if (ones > 0) yield return $"{ones}.mp3";
                yield break;
            }
            foreach (var ch in number.ToString())
                yield return $"{ch}.mp3";
        }

        private IEnumerable<string> BuildTicketNumberSequence(string ticketNumber)
        {
            if (!TryExtractSequence(ticketNumber, out int seq))
            {
                foreach (var ch in ticketNumber)
                    if (char.IsDigit(ch)) yield return $"{ch}.mp3";
                yield break;
            }
            foreach (var f in BuildNumberAudio(seq))
                yield return f;
        }

        // GANTI PlayChimeAndTts menjadi enqueue + start sequence
        private async Task PlayChimeAndTts(string ticketNumber, int counter)
        {
            await _audioLock.WaitAsync();
            _releaseLockWhenEmpty = true;
            try
            {
                var list = new List<string>();

                void Add(string name)
                {
                    var full = FindAudio(name);
                    if (full != null) list.Add(full);
                    else Serilog.Log.Warning("Missing audio: {File}", name);
                }

                // chime → "nomor_antrian" → nomor tiket → "silakan_menuju_ke_loket" → nomor loket
                Add("chime.mp3");
                Add("nomor_antrian.mp3");
                foreach (var f in BuildTicketNumberSequence(ticketNumber)) Add(f);
                Add("silakan_menuju_ke_loket.mp3");
                foreach (var f in BuildNumberAudio(counter)) Add(f);

                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var f in list) _audioQueue.Enqueue(f);
                    StartIfIdle();
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "PlayChimeAndTts");
                _releaseLockWhenEmpty = false;
                _audioLock.Release();
            }
        }

        // Inisialisasi player sekali
        private void EnsureVoicePlayer()
        {
           if (_voicePlayer != null) return;
           _voicePlayer = new WpfMediaPlayer();
            _voicePlayer.MediaEnded += VoicePlayer_MediaEnded;
            _voicePlayer.MediaFailed += VoicePlayer_MediaFailed;
            _voicePlayer.Volume = 1.0;
            _voicePlayer.IsMuted = false;
        }

        // Enqueue dan mulai jika idle (dipanggil dari PlayChimeAndTts)
        private void StartIfIdle()
        {
            EnsureVoicePlayer();
            if (_sequenceActive) return;
            _sequenceActive = true;
            PlayNextInQueue();
        }

        private async void VoicePlayer_MediaEnded(object? sender, EventArgs e)
        {
            // beri jeda kecil sebelum lanjut
            if (GapMs > 0) await Task.Delay(GapMs);
            PlayNextInQueue();
        }

        private void VoicePlayer_MediaFailed(object? sender, ExceptionEventArgs e)
        {
            Serilog.Log.Warning("Audio failed: {Msg}", e.ErrorException?.Message);
            PlayNextInQueue();
        }

        // Putar item berikutnya (serial, 1 per waktu)
        private void PlayNextInQueue()
        {
            if (_audioQueue.Count == 0)
            {
                _sequenceActive = false;
                if (_releaseLockWhenEmpty)
                {
                    _releaseLockWhenEmpty = false;
                    _audioLock.Release();
                }
                return;
            }

            var file = _audioQueue.Dequeue();
            try
            {
                _voicePlayer!.Stop();                 // pastikan reset
                _voicePlayer.Position = TimeSpan.Zero;
                _voicePlayer.Open(new Uri(file, UriKind.Absolute));
                _voicePlayer.Play();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "PlayNext failed {File}", file);
                // Skip ke item berikut
                PlayNextInQueue();
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        }

        // --- Handlers MediaElement (FIX: hilang) ---
        private void Video_MediaOpened(object? sender, RoutedEventArgs e)
        {
            if (sender is MediaElement m)
            {
                m.Position = TimeSpan.Zero;
                m.Play();
                Log.Information("MediaElement opened");
            }
        }

        private void Video_MediaEnded(object? sender, RoutedEventArgs e)
        {
            if (sender is MediaElement m)
            {
                m.Position = TimeSpan.Zero;
                m.Play(); // loop
            }
        }

        private void Video_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            Log.Warning("MediaElement failed: {Msg}", e.ErrorException?.Message);
            if (sender is MediaElement m) m.Visibility = Visibility.Collapsed;
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

        private static string? FindResourceFile(string fileName)
        {
            var roots = new[]
            {
                AppContext.BaseDirectory,
                Path.Combine(AppContext.BaseDirectory, "Resources"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Resources")),
                Path.Combine(Directory.GetCurrentDirectory(), "Resources")
            };
            foreach (var r in roots)
            {
                var p = Path.Combine(r, fileName);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
            return null;
        }

        private void BuildLogoList()
        {
            _logoFiles.Clear();
            foreach (var n in new[] { "logo.png", "posaja.png", "pospay.png" })
            {
                var p = FindResourceFile(n);
                if (p != null) _logoFiles.Add(p);
            }
            Log.Information("Logos found: {Count} -> {Files}", _logoFiles.Count, string.Join(", ", _logoFiles));
        }

        private void StartLogoRotation(bool enabled)
        {
            var host = this.FindName("LogoHost") as FrameworkElement;
            var imgA = this.FindName("LogoImageA") as Image;
            var imgB = this.FindName("LogoImageB") as Image;
            if (host == null || imgA == null || imgB == null) return;

            if (!enabled)
            {
                _logoTimer.Stop();
                _logoTimer.Tick -= LogoTimer_Tick;
                _logoRotationStarted = false;
                host.Visibility = Visibility.Collapsed;
                return;
            }

            host.Visibility = Visibility.Visible;

            // Jika sudah running, biarkan timer berjalan (hindari reset setiap polling)
            if (_logoRotationStarted) return;

            BuildLogoList();
            if (_logoFiles.Count == 0) return;

            _logoIndex = -1;
            _logoUseA = true;
            ShowNextLogo(immediate: true);

            _logoTimer.Stop();
            _logoTimer.Tick -= LogoTimer_Tick;
            _logoTimer.Interval = TimeSpan.FromSeconds(6);
            _logoTimer.Tick += LogoTimer_Tick;
            _logoTimer.Start();
            _logoRotationStarted = true;
        }

        private void LogoTimer_Tick(object? sender, EventArgs e) => ShowNextLogo(immediate: false);

        private void ShowNextLogo(bool immediate)
        {
            var imgA = this.FindName("LogoImageA") as Image;
            var imgB = this.FindName("LogoImageB") as Image;
            if (imgA == null || imgB == null || _logoFiles.Count == 0) return;

            _logoIndex = (_logoIndex + 1) % _logoFiles.Count;
            var nextPath = _logoFiles[_logoIndex];

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(nextPath, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();

            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(450) };
            var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(450) };

            if (_logoUseA)
            {
                imgB.Source = bi;
                if (immediate) { imgA.Opacity = 0; imgB.Opacity = 1; }
                else { imgA.BeginAnimation(UIElement.OpacityProperty, fadeOut); imgB.BeginAnimation(UIElement.OpacityProperty, fadeIn); }
            }
            else
            {
                imgA.Source = bi;
                if (immediate) { imgA.Opacity = 1; imgB.Opacity = 0; }
                else { imgB.BeginAnimation(UIElement.OpacityProperty, fadeOut); imgA.BeginAnimation(UIElement.OpacityProperty, fadeIn); }
            }
            _logoUseA = !_logoUseA;
        }

        // DTO
        private sealed class TicketDto
        {
            // Tambahan agar .ThenByDescending(x => x.Id) tidak error
            public int Id { get; set; } = 0;
            public string Number { get; set; } = "";
            public int Counter { get; set; }
            public string Status { get; set; } = "";
            public DateTime CalledAt { get; set; } = DateTime.Now;
            public DateTime Time
            {
                get => CalledAt;
                set => CalledAt = value;
            }
            public string TicketNumber
            {
                get => Number;
                set => Number = value;
            }
            public int? CounterNumber
            {
                get => Counter;
                set { if (value.HasValue) Counter = value.Value; }
            }
            public override string ToString() => $"{Number} (L{Counter})";
        }
    }
}