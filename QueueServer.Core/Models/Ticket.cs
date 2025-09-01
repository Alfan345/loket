using Shared.Enums;

namespace QueueServer.Core.Models;

/// <summary>
/// Representasi satu tiket antrian.
/// </summary>
public class Ticket
{
    public int Id { get; set; }

    /// <summary>Nomor tiket final, contoh "A-023".</summary>
    public string TicketNumber { get; set; } = default!;

    /// <summary>Urutan incremental harian.</summary>
    public int Sequence { get; set; }

    public DateOnly Date { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.WAITING;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CalledAt { get; set; }
    public DateTime? ServingStartAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Nomor loket (1..5) yang menangani tiket.</summary>
    public int? CounterNumber { get; set; }

    public int LastRecallCount { get; set; }

    public string? Notes { get; set; }
}