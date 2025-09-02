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

namespace DisplayApp.Wpf;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:5000") };
    private HubConnection? _hub;
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _marqueeTimer = new();
    private double _marqueeX = 0;
    private string _runningText = "";
    private string? _videoPath;
    private string? _logoPath;

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
            RecentCallsList.ItemsSource = _recentCalls;
            await LoadSettings();
            await LoadInitialActive();
            await InitHub();
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
            RunningTextBlock.Text = _runningText;

            settings.TryGetValue("LogoPath", out _logoPath);
            settings.TryGetValue("VideoPath", out _videoPath);
            settings.TryGetValue("ShowLogo", out var showLogo);
            settings.TryGetValue("ShowVideo", out var showVideo);

            if (!string.IsNullOrWhiteSpace(_logoPath) && File.Exists(_logoPath) && showLogo == "true")
            {
                try
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(_logoPath)));
                    LogoImage.Source = bi;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Logo load fail");
                    LogoImage.Source = null;
                }
            }
            else
            {
                LogoImage.Source = null;
            }

            if (showVideo == "true")
                LoadAndPlayVideo();
            else
                VideoPlayer.Visibility = Visibility.Collapsed;
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
                    ticketNumber = je.GetPropertyOrNull("TicketNumber")?.GetString();
                    counter = je.GetPropertyOrNull("CounterNumber")?.GetInt32() ?? 0;
                }
                else
                {
                    dynamic d = payload!;
                    ticketNumber = d?.TicketNumber;
                    counter = d?.CounterNumber ?? 0;
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
        ActiveNumberText.Text = TryExtractSequence(ticketNumber, out int seq)
            ? seq.ToString()
            : ticketNumber;

        ActiveCounterText.Text = $"Ke Loket {counter}";
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

    private void AnimateActiveFlash()
    {
        var anim = new ColorAnimation
        {
            From = Colors.White,
            To = Colors.Yellow,
            Duration = TimeSpan.FromMilliseconds(200),
            AutoReverse = true
        };
        var brush = new SolidColorBrush(Colors.White);
        ActiveNumberText.Foreground = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private void InitClock()
    {
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            DateText.Text = DateTime.Now.ToString("dddd, dd MMMM yyyy");
        };
        _clockTimer.Start();
    }

    private void InitMarquee()
    {
        _marqueeTimer.Interval = TimeSpan.FromMilliseconds(40);
        _marqueeTimer.Tick += (_, _) =>
        {
            _marqueeX -= 2;
            if (_marqueeX < -RunningTextBlock.ActualWidth)
                _marqueeX = ActualWidth;
            Canvas.SetLeft(RunningTextBlock, _marqueeX);
            Canvas.SetTop(RunningTextBlock, 20 - RunningTextBlock.FontSize / 2);
        };
        _marqueeTimer.Start();
    }

    private void LoadAndPlayVideo()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_videoPath) && File.Exists(_videoPath))
            {
                VideoPlayer.Source = new Uri(Path.GetFullPath(_videoPath));
                VideoPlayer.MediaEnded += (_, _) =>
                {
                    VideoPlayer.Position = TimeSpan.Zero;
                    VideoPlayer.Play();
                };
                VideoPlayer.Play();
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