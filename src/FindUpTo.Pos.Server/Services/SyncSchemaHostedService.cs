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

        var customerColumns = await GetColumnsAsync(db, "Customers", cancellationToken);
        if (!customerColumns.Contains("CustomerAccessTokenHash", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Customers ADD COLUMN CustomerAccessTokenHash TEXT NOT NULL DEFAULT '';", cancellationToken);

        var paymentColumns = await GetColumnsAsync(db, "Payments", cancellationToken);
        if (!paymentColumns.Contains("ClientOperationId", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Payments ADD COLUMN ClientOperationId TEXT NOT NULL DEFAULT '';", cancellationToken);

        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Payments_ClientOperationId ON Payments (ClientOperationId) WHERE ClientOperationId <> '';", cancellationToken);
    }

    private static async Task<HashSet<string>> GetColumnsAsync(CoreDbContext db, string tableName, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetString(1));
        return result;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
