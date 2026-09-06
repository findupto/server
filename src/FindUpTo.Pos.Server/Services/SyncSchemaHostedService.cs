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
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS RelayDevices (Id INTEGER NOT NULL CONSTRAINT PK_RelayDevices PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, SecretHash TEXT NOT NULL, Active INTEGER NOT NULL DEFAULT 1, CreatedAtUtc TEXT NOT NULL, LastSeenAtUtc TEXT NULL);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_RelayDevices_Name ON RelayDevices (Name);", cancellationToken);

        var hasCustomerAccessTokenHash = false;
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(Customers);";
            if (command.Connection!.State != System.Data.ConnectionState.Open)
                await command.Connection.OpenAsync(cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "CustomerAccessTokenHash", StringComparison.OrdinalIgnoreCase))
                {
                    hasCustomerAccessTokenHash = true;
                    break;
                }
            }
        }

        if (!hasCustomerAccessTokenHash)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Customers ADD COLUMN CustomerAccessTokenHash TEXT NOT NULL DEFAULT '';", cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
