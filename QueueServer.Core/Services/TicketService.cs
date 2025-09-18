using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueueServer.Core.Data;
using QueueServer.Core.Models;
using Shared.Enums;

namespace QueueServer.Core.Services;

public class TicketService : ITicketService
{
    private readonly QueueDbContext _db;
    private readonly ISettingsService _settings;
    private readonly ILogger<TicketService> _logger;
    public TicketService(QueueDbContext db, ISettingsService settings, ILogger<TicketService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    private DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public async Task<Ticket> CreateTicketAsync()
    {
        var prefix = await _settings.GetValueAsync("Prefix") ?? "A";
        var today = Today;
        var maxSeq = await _db.Tickets.Where(t => t.Date == today).MaxAsync(t => (int?)t.Sequence) ?? 0;
        var nextSeq = maxSeq + 1;
        var ticket = new Ticket
        {
            Date = today,
            Sequence = nextSeq,
            // Hilangkan leading zero (sebelumnya :000)
            TicketNumber = $"{prefix}-{nextSeq}",
            Status = TicketStatus.WAITING
        };
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created ticket {Ticket}", ticket.TicketNumber);
        return ticket;
    }

    public async Task<Ticket?> CallNextAsync(int counterNumber)
{
    var today = Today;

    // CARI tiket WAITING
    var ticket = await _db.Tickets
        .Where(t => t.Date == today && t.Status == TicketStatus.WAITING)
        .OrderBy(t => t.Sequence)
        .FirstOrDefaultAsync();

    // JIKA BELUM ADA → BUAT SEKALIGUS
    if (ticket == null)
    {
        var prefix = await _settings.GetValueAsync("Prefix") ?? "A";
        var maxSeq = await _db.Tickets.Where(t => t.Date == today).MaxAsync(t => (int?)t.Sequence) ?? 0;
        ticket = new Ticket
        {
            Date = today,
            Sequence = maxSeq + 1,
            TicketNumber = $"{(maxSeq + 1)}", // tanpa prefix & tanpa leading zero
            Status = TicketStatus.WAITING
        };
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
    }

    // Ubah status jadi CALLING
    ticket.Status = TicketStatus.CALLING;
    ticket.CounterNumber = counterNumber;
    ticket.CalledAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();
    _logger.LogInformation("Calling ticket {Ticket} at counter {Counter}", ticket.TicketNumber, counterNumber);
    return ticket;
}

    public async Task<Ticket?> RecallAsync(int ticketId)
    {
        var ticket = await _db.Tickets.FindAsync(ticketId);
        if (ticket == null) return null;
        if (ticket.Status is TicketStatus.CALLING or TicketStatus.NO_SHOW)
        {
            ticket.Status = TicketStatus.CALLING;
            ticket.CalledAt = DateTime.UtcNow;
            ticket.LastRecallCount += 1;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Recalled ticket {Ticket}", ticket.TicketNumber);
        }
        return ticket;
    }

    public async Task<Ticket?> StartServingAsync(int ticketId)
    {
        var ticket = await _db.Tickets.FindAsync(ticketId);
        if (ticket == null) return null;
        if (ticket.Status == TicketStatus.CALLING)
        {
            ticket.Status = TicketStatus.SERVING;
            ticket.ServingStartAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Started serving {Ticket}", ticket.TicketNumber);
        }
        return ticket;
    }

    public async Task<Ticket?> CompleteAsync(int ticketId)
    {
        var ticket = await _db.Tickets.FindAsync(ticketId);
        if (ticket == null) return null;
        if (ticket.Status == TicketStatus.SERVING)
        {
            ticket.Status = TicketStatus.DONE;
            ticket.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Completed ticket {Ticket}", ticket.TicketNumber);
        }
        return ticket;
    }

    public async Task<Ticket?> SkipAsync(int ticketId)
    {
        var ticket = await _db.Tickets.FindAsync(ticketId);
        if (ticket == null) return null;
        if (ticket.Status == TicketStatus.CALLING)
        {
            ticket.Status = TicketStatus.NO_SHOW;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Skipped ticket {Ticket}", ticket.TicketNumber);
        }
        return ticket;
    }

    public async Task<IReadOnlyList<Ticket>> GetWaitingAsync(int take = 10)
    {
        var today = Today;
        return await _db.Tickets.Where(t => t.Date == today && t.Status == TicketStatus.WAITING)
            .OrderBy(t => t.Sequence).Take(take).ToListAsync();
    }

    public async Task<Ticket?> GetActiveCallingAsync(int counterNumber)
    {
        var today = Today;
        return await _db.Tickets.Where(t => t.Date == today
            && (t.Status == TicketStatus.CALLING || t.Status == TicketStatus.SERVING)
            && t.CounterNumber == counterNumber)
            .OrderByDescending(t => t.CalledAt).FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<Ticket>> GetTodayTicketsAsync()
    {
        var today = Today;
        return await _db.Tickets.Where(t => t.Date == today)
            .OrderBy(t => t.Sequence).ToListAsync();
    }

    public async Task UpdatePrefixAsync(string prefix)
    {
        await _settings.SetValueAsync("Prefix", prefix);
        _logger.LogInformation("Updated prefix to {Prefix}", prefix);
    }
}