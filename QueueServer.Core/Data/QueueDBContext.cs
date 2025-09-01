using Microsoft.EntityFrameworkCore;
using QueueServer.Core.Models;

namespace QueueServer.Core.Data;

public class QueueDbContext : DbContext
{
    public QueueDbContext(DbContextOptions<QueueDbContext> options) : base(options) { }

    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Setting>().HasKey(s => s.Key);
        modelBuilder.Entity<Ticket>().HasIndex(t => t.Date);
    }
}