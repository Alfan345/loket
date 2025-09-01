using QueueServer.Core.Models;

namespace QueueServer.Core.Services;

public interface ITicketService
{
    Task<Ticket> CreateTicketAsync();
    Task<Ticket?> CallNextAsync(int counterNumber);
    Task<Ticket?> RecallAsync(int ticketId);
    Task<Ticket?> StartServingAsync(int ticketId);
    Task<Ticket?> CompleteAsync(int ticketId);
    Task<Ticket?> SkipAsync(int ticketId);
    Task<IReadOnlyList<Ticket>> GetWaitingAsync(int take = 10);
    Task<Ticket?> GetActiveCallingAsync(int counterNumber);
    Task<IReadOnlyList<Ticket>> GetTodayTicketsAsync();
    Task UpdatePrefixAsync(string prefix);
}