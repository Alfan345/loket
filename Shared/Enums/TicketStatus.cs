namespace Shared.Enums;

/// <summary>
/// Status tiket dalam siklus antrian.
/// </summary>
public enum TicketStatus
{
    WAITING,
    CALLING,
    SERVING,
    DONE,
    NO_SHOW,
    CANCELED
}