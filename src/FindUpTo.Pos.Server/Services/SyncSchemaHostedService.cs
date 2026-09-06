using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class SyncSchemaHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS SyncOperations (Id INTEGER NOT NULL CONSTRAINT PK_SyncOperations PRIMARY KEY AUTOINCREMENT, ClientOperationId TEXT NOT NULL, OrderId INTEGER NOT NULL, CreatedByUsername TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_SyncOperations_ClientOperationId ON SyncOperations (ClientOperationId);", cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
