using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.IO;

namespace QueueServer.Core.Data;

public class QueueDbContextFactory : IDesignTimeDbContextFactory<QueueDbContext>
{
    public QueueDbContext CreateDbContext(string[] args)
    {
        // Samakan dengan runtime: %ProgramData%\QueueSystem\queue.db
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "QueueSystem");
        Directory.CreateDirectory(dataRoot);
        var dbPath = Path.Combine(dataRoot, "queue.db");

        var options = new DbContextOptionsBuilder<QueueDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new QueueDbContext(options);
    }
}