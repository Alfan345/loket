using Microsoft.AspNetCore.SignalR;
using QueueServer.Api.Hubs;
using Shared.Contracts;

namespace QueueServer.Api.Utils;

/// <summary>
/// Helper broadcast event ke semua client.
/// </summary>
public class HubBroadcaster
{
    private readonly IHubContext<QueueHub> _hub;
    public HubBroadcaster(IHubContext<QueueHub> hub) => _hub = hub;

    public Task TicketCreated(object dto) =>
        _hub.Clients.All.SendAsync(HubEvents.TicketCreated, dto);

    public Task TicketUpdated(object dto) =>
        _hub.Clients.All.SendAsync(HubEvents.TicketUpdated, dto);

    public Task TicketCalled(object dto) =>
        _hub.Clients.All.SendAsync(HubEvents.TicketCalled, dto);

    public Task SettingsChanged(object dto) =>
        _hub.Clients.All.SendAsync(HubEvents.SettingsChanged, dto);
}