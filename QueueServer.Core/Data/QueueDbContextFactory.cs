using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QueueServer.Core.Data;

public class QueueDbContextFactory : IDesignTimeDbContextFactory<QueueDbContext>
{
    public QueueDbContext CreateDbContext(string[] args)
    {
        // Mirror runtime path logic: AppContext.BaseDirectory/App_Data/queue.db
        var baseDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(baseDir, "App_Data");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "queue.db");

        var options = new DbContextOptionsBuilder<QueueDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new QueueDbContext(options);
    }
}