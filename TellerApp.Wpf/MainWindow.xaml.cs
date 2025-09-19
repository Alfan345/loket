using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls; // for controls referenced by name
using Microsoft.AspNetCore.SignalR.Client;
using Shared.Contracts; // gunakan konstanta HubEvents
using Serilog;

// NOTE: Ubah base address jika port server bukan 5000
namespace TellerApp.Wpf
{
public partial class MainWindow : Window
{
    private HubConnection? _hub;
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:5000") };
    private string _loket = "1";
    private bool _windowLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Log.Information("Teller MainWindow constructed");
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += async (_, _) =>
        {
            _windowLoaded = true;
            SetStatus("Menunggu koneksi...");
            // Auto connect sekali saat startup
            await ConnectAsync();
        };
    }

    private static string WithoutPrefix(string ticketNumber)
    {
        // Format "A-023" => "023"
        var parts = ticketNumber.Split('-', '_');
        return parts.Length > 1 ? parts[1] : ticketNumber;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (_hub is { State: HubConnectionState.Connected }) return;
        if (ConnectButton != null) ConnectButton.IsEnabled = false;
        SetStatus("Menghubungkan...");
        try
        {
            _hub = new HubConnectionBuilder()
                .WithUrl("http://localhost:5000/hub/queue") // server maps MapHub<QueueHub>("/hub/queue")
                .WithAutomaticReconnect()
                .Build();

            // Terima event TicketCalled dari server
            _hub.On<TicketCalledEvent>("TicketCalled", e =>
            {
                if (e.CounterNumber?.ToString() == _loket)
                {
                    Dispatcher.Invoke(() =>
                    {
                        CurrentNumberText.Text = WithoutPrefix(e.TicketNumber);
                        LoketLabel.Text = $"Loket: {e.CounterNumber}";
                        SetStatus("Dipanggil");
                    });
                }
            });

            await _hub.StartAsync();
            SetStatus("Terhubung");
            if (NextButton != null)
            {
                NextButton.IsEnabled = true;
                NextButton.Visibility = Visibility.Visible;
            }
            if (ConnectButton != null)
            {
                ConnectButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            SetStatus("Gagal konek");
            if (ConnectButton != null)
            {
                ConnectButton.Content = "Coba Lagi";
                ConnectButton.IsEnabled = true;
                ConnectButton.Visibility = Visibility.Visible;
            }
            Console.WriteLine(ex);
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        NextButton.IsEnabled = false;
        SetStatus("Memanggil...");
        try
        {
            var resp = await _http.PostAsync($"/api/queue/next/{_loket}", null);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                SetStatus("Kosong");
            }
            else if (resp.IsSuccessStatusCode)
            {
                var ticket = await resp.Content.ReadFromJsonAsync<TicketResponse>();
                if (ticket != null)
                {
                    CurrentNumberText.Text = WithoutPrefix(ticket.TicketNumber);
                    LoketLabel.Text = $"Loket: {ticket.CounterNumber}";
                    SetStatus("Dipanggil");
                }
            }
            else
            {
                SetStatus("Gagal panggil");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Error panggil");
            Console.WriteLine(ex);
        }
        finally
        {
            NextButton.IsEnabled = true;
        }
    }

    private void SetStatus(string s)
    {
        if (!_windowLoaded)
        {
            // Tunda sampai Loaded untuk menghindari NullReference.
            Dispatcher.BeginInvoke(() => SetStatus(s));
            return;
        }
        StatusText.Text = $"Status: {s}";
    }

    private void LoketCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LoketCombo.SelectedItem is ComboBoxItem item && item.Content is string s && !string.IsNullOrWhiteSpace(s))
        {
            _loket = s.Trim();
            // Hindari NullReference saat initialization; hanya update status jika window sudah loaded.
            SetStatus($"Loket {_loket} dipilih");
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_http) { Owner = this };
        var ok = dlg.ShowDialog() == true;
        if (ok)
            SetStatus("Pengaturan disimpan");
        else
            SetStatus("Pengaturan dibatalkan");
        await Task.CompletedTask;
    }
}

// Kelas untuk respon POST /api/queue/next/{counter}
public class TicketResponse
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? CounterNumber { get; set; }
}

// Event broadcast dari server (TicketCalled)
public class TicketCalledEvent
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public int? CounterNumber { get; set; }
}
}