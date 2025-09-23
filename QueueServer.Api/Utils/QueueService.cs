using Microsoft.EntityFrameworkCore;
using QueueServer.Core.Data;

namespace QueueServer.Api.Utils;

// Layanan reset antrian (harian)
public class QueueService
{
    private readonly QueueDbContext _db;
    public QueueService(QueueDbContext db) => _db = db;

    public async Task ResetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var rows = await _db.Tickets.Where(t => t.Date == today).ToListAsync();
        _db.Tickets.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }
}