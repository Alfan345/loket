using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using System.Net.Http;

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
            // Normalisasi angka -> string (jaga-jaga kalau server masih numeric)
            foreach (var tk in tickets)
            {
                tk.Status = tk.Status switch
                {
                    "0" => "WAITING",
                    "1" => "CALLING",
                    "2" => "SERVING",
                    "3" => "DONE",
                    "4" => "NO_SHOW",
                    "5" => "CANCELED",
                    _ => tk.Status
                };
            }

            TicketsGrid.ItemsSource = tickets;

            var waiting = tickets.Where(t => t.Status == "WAITING").Take(5).ToList();
            WaitingList.ItemsSource = waiting;

            var counter = GetSelectedCounter();
            var active = tickets.FirstOrDefault(t =>
                t.Status == "CALLING" && t.CounterNumber == counter);

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

    // =========== EVENT HANDLERS ===========

    private async void CallNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var counter = GetSelectedCounter();
            var resp = await _http.PostAsync($"/api/queue/next/{counter}", null);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                MessageBox.Show($"Gagal panggil berikutnya ({(int)resp.StatusCode})\n{body}");
            }
            await RefreshTickets();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CallNext_Click");
            MessageBox.Show("Error Call Next. Lihat log.");
        }
    }

    // Recall otomatis tiket CALLING di loket ini (tanpa pilih grid)
    private async void Recall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var counter = GetSelectedCounter();
            var tickets = await _http.GetFromJsonAsync<List<TicketDto>>("/api/tickets/today") ?? new();
            foreach (var tk in tickets)
            {
                tk.Status = tk.Status switch
                {
                    "0" => "WAITING",
                    "1" => "CALLING",
                    "2" => "SERVING",
                    "3" => "DONE",
                    "4" => "NO_SHOW",
                    "5" => "CANCELED",
                    _ => tk.Status
                };
            }

            var active = tickets.FirstOrDefault(t => t.Status == "CALLING" && t.CounterNumber == counter);
            if (active == null)
            {
                MessageBox.Show("Tidak ada tiket CALLING di loket ini.");
                return;
            }
            var resp = await _http.PostAsync($"/api/tickets/{active.Id}/recall", null);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                MessageBox.Show($"Recall gagal ({(int)resp.StatusCode})\n{body}");
            }
            await RefreshTickets();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recall_Click");
            MessageBox.Show("Error recall. Lihat log.");
        }
    }

    private async void SavePrefix_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var newPrefix = PrefixBox.Text.Trim();
            if (string.IsNullOrEmpty(newPrefix))
            {
                MessageBox.Show("Prefix kosong.");
                return;
            }
            var content = JsonContent.Create(new Dictionary<string, string> { { "Prefix", newPrefix } });
            var resp = await _http.PutAsync("/api/settings", content);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                MessageBox.Show($"Simpan Prefix gagal ({(int)resp.StatusCode})\n{body}");
            }
            else
            {
                await LoadSettingsAsync();
                MessageBox.Show("Prefix disimpan.");
            }
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
            var resp = await _http.PutAsync("/api/settings", content);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                MessageBox.Show($"Simpan Running Text gagal ({(int)resp.StatusCode})\n{body}");
            }
            else
            {
                MessageBox.Show("Running Text disimpan.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SaveRunningText_Click");
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
    public DateTime? CompletedAt { get; set; }
}