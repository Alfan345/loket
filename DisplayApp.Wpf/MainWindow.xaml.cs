using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls;
using System.IO;
using Serilog;
// Removed: using QueueServer.Core.Utilities; (namespace does not exist)
using System.Net.Http;
using System.Speech.Synthesis;

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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Focus(); // agar KeyDown aktif
            await InitAsync();
        };
    }

    private async Task InitAsync()
    {
        try
        {
            await LoadSettings();
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
                catch (Exception ex) { Log.Warning(ex, "Logo load fail"); LogoImage.Source = null; }
            }
            else LogoImage.Source = null;

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

    private async Task InitHub()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl("http://localhost:5000/hub/queue")
            .WithAutomaticReconnect()
            .Build();

        _hub.On<dynamic>("TicketCalled", data =>
        {
            try
            {
                string ticketNumber = data?.TicketNumber;
                int counter = data?.CounterNumber;
                Dispatcher.Invoke(() =>
                {
                    SetActive(ticketNumber, counter);
                    AnimateActiveFlash();
                    _ = RefreshNextList();
                    _ = PlayChimeAndTts(ticketNumber, counter);
                });
            }
            catch (Exception ex) { Log.Error(ex, "TicketCalled handler"); }
        });

        _hub.On<dynamic>("TicketUpdated", _ => Dispatcher.Invoke(() => _ = RefreshNextList()));
        _hub.On<dynamic>("TicketCreated", _ => Dispatcher.Invoke(() => _ = RefreshNextList()));
        _hub.On<Dictionary<string, string>>("SettingsChanged", dict =>
        {
            try
            {
                if (dict.TryGetValue("RunningText", out var rt))
                {
                    _runningText = rt;
                    Dispatcher.Invoke(() => RunningTextBlock.Text = _runningText);
                }
                if (dict.ContainsKey("LogoPath") || dict.ContainsKey("VideoPath"))
                    Dispatcher.Invoke(async () => await LoadSettings());
            }
            catch (Exception ex) { Log.Error(ex, "SettingsChanged handler"); }
        });

        await _hub.StartAsync();
        Log.Information("Hub connected (Display)");
        await RefreshNextList();
    }

    private async Task RefreshNextList()
    {
        try
        {
            var tickets = await _http.GetFromJsonAsync<List<TicketDto>>("/api/tickets/today") ?? new();
            var calling = tickets.Where(t => t.Status == "CALLING")
                                 .OrderByDescending(t => t.CalledAt)
                                 .FirstOrDefault();
            if (calling != null)
                SetActive(calling.TicketNumber, calling.CounterNumber ?? 0);

            var waiting = tickets.Where(t => t.Status == "WAITING")
                                 .OrderBy(t => t.Sequence)
                                 .Take(5)
                                 .Select(t => t.TicketNumber)
                                 .ToList();
            NextList.ItemsSource = waiting;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RefreshNextList");
        }
    }

    private void SetActive(string ticketNumber, int counter)
    {
        ActiveNumberText.Text = ticketNumber;
        CounterText.Text = $"Ke Loket {counter}";
    }

    private void AnimateActiveFlash()
    {
        var anim = new ColorAnimation
        {
            From = Colors.White,
            To = Colors.Yellow,
            Duration = TimeSpan.FromMilliseconds(300),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3)
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
            Canvas.SetTop(RunningTextBlock, 0);
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

    private void ReloadVideo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            VideoPlayer.Stop();
            LoadAndPlayVideo();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ReloadVideo_Click");
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

                string verbalNomor = ticketNumber;
                if (NumberToBahasa.TryParseSequenceFromTicketNumber(ticketNumber, out int seq))
                {
                    var words = NumberToBahasa.ToWords(seq);
                    var prefix = ticketNumber.Split('-', '_')[0];
                    verbalNomor = $"{prefix} {words}";
                }
                var verbalLoket = NumberToBahasa.LoketToWords(counter);
                var kalimat = $"Nomor antrian {verbalNomor}, silakan ke loket {verbalLoket}.";
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

public class TicketDto
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public int? CounterNumber { get; set; }
    public int Sequence { get; set; }
    public DateTime? CalledAt { get; set; }
}