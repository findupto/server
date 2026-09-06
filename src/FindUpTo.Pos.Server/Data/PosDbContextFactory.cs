using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FindUpTo.Pos.Server.Data;

public sealed class PosDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>();
        options.UseSqlite(Environment.GetEnvironmentVariable("POS_MIGRATION_CONNECTION") ?? "Data Source=pos.db");
        return new CoreDbContext(options.Options);
    }
}
