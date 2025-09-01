using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using System.Net.Http;
// Removed invalid using directive for QueueServer.Core.Utilities

// NOTE: Ubah base address jika port server bukan 5000
namespace TellerApp.Wpf;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:5000") };
    private HubConnection? _hub;

    public MainWindow()
    {
        InitializeComponent();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            await LoadSettingsAsync();
            await RefreshTickets();
            await ConnectHub();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InitAsync failed");
            MessageBox.Show("Gagal init aplikasi. Lihat log.");
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var dict = await _http.GetFromJsonAsync<Dictionary<string, string>>("/api/settings");
            if (dict != null)
            {
                if (dict.TryGetValue("Prefix", out var p)) PrefixBox.Text = p;
                if (dict.TryGetValue("RunningText", out var r)) RunningTextBox.Text = r;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LoadSettingsAsync");
        }
    }

    private async Task RefreshTickets()
    {
        try
        {
            var tickets = await _http.GetFromJsonAsync<List<TicketDto>>("/api/tickets/today") ?? new();
            TicketsGrid.ItemsSource = tickets;

            var waiting = tickets.Where(t => t.Status == "WAITING").Take(5).ToList();
            WaitingList.ItemsSource = waiting;

            var counter = GetSelectedCounter();
            var active = tickets.FirstOrDefault(t =>
                (t.Status == "CALLING" || t.Status == "SERVING") && t.CounterNumber == counter);

            ActiveTicketText.Text = active?.TicketNumber ?? "-";
            ActiveTicketStatus.Text = active?.Status ?? "";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RefreshTickets");
        }
    }

    private int GetSelectedCounter()
    {
        if (LoketCombo.SelectedItem is ComboBoxItem c &&
            int.TryParse(c.Content?.ToString(), out int v))
            return v;
        return 1;
    }

    private async Task ConnectHub()
    {
        try
        {
            _hub = new HubConnectionBuilder()
                .WithUrl("http://localhost:5000/hub/queue")
                .WithAutomaticReconnect()
                .Build();

            _hub.On<object>("TicketCreated", async _ => await RefreshTickets());
            _hub.On<object>("TicketUpdated", async _ => await RefreshTickets());
            _hub.On<object>("TicketCalled", async _ => await RefreshTickets());
            _hub.On<object>("SettingsChanged", async _ => await LoadSettingsAsync());

            await _hub.StartAsync();
            Log.Information("Hub connected");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ConnectHub");
        }
    }

    // =========== EVENT HANDLERS (harus cocok dengan XAML) ===========

    private async void CallNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var counter = GetSelectedCounter();
            var resp = await _http.PostAsync($"/api/queue/next/{counter}", null);
            if (!resp.IsSuccessStatusCode)
                MessageBox.Show("Tidak ada tiket WAITING.");
            await RefreshTickets();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CallNext_Click");
        }
    }

    private async void Recall_Click(object sender, RoutedEventArgs e)
    {
        if (TicketsGrid.SelectedItem is TicketDto t)
        {
            try
            {
                await _http.PostAsync($"/api/tickets/{t.Id}/recall", null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Recall_Click");
            }
        }
    }

    private async void ServeStart_Click(object sender, RoutedEventArgs e)
    {
        if (TicketsGrid.SelectedItem is TicketDto t)
        {
            try
            {
                await _http.PostAsync($"/api/tickets/{t.Id}/serveStart", null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ServeStart_Click");
            }
        }
    }

    private async void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (TicketsGrid.SelectedItem is TicketDto t)
        {
            try
            {
                await _http.PostAsync($"/api/tickets/{t.Id}/skip", null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Skip_Click");
            }
        }
    }

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (TicketsGrid.SelectedItem is TicketDto t)
        {
            try
            {
                await _http.PostAsync($"/api/tickets/{t.Id}/complete", null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Complete_Click");
            }
        }
    }

    private async void SavePrefix_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = JsonContent.Create(new Dictionary<string, string> { { "Prefix", PrefixBox.Text } });
            await _http.PutAsync("/api/settings", content);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SavePrefix_Click");
        }
    }

    private async void SaveRunningText_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = JsonContent.Create(new Dictionary<string, string> { { "RunningText", RunningTextBox.Text } });
            await _http.PutAsync("/api/settings", content);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SaveRunningText_Click");
        }
    }
}

// DTO sederhana
public class TicketDto
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public int? CounterNumber { get; set; }
    public DateTime? CalledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}