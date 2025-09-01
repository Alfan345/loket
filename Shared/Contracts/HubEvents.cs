namespace Shared.Contracts;

/// <summary>
/// Nama event SignalR yang digunakan antar aplikasi.
/// </summary>
public static class HubEvents
{
    public const string TicketCreated = "TicketCreated";
    public const string TicketUpdated = "TicketUpdated";
    public const string TicketCalled = "TicketCalled";
    public const string SettingsChanged = "SettingsChanged";
}