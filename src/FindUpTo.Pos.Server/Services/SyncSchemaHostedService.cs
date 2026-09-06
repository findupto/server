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
        if (!customerColumns.Contains("CustomerAccessTokenHash", StringComparer.OrdinalIgnoreCase)) await db.Database.ExecuteSqlRawAsync("ALTER TABLE Customers ADD COLUMN CustomerAccessTokenHash TEXT NOT NULL DEFAULT '';", cancellationToken);
        var paymentColumns = await GetColumnsAsync(db, "Payments", cancellationToken);
        if (!paymentColumns.Contains("ClientOperationId", StringComparer.OrdinalIgnoreCase)) await db.Database.ExecuteSqlRawAsync("ALTER TABLE Payments ADD COLUMN ClientOperationId TEXT NOT NULL DEFAULT '';", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Payments_ClientOperationId ON Payments (ClientOperationId) WHERE ClientOperationId <> '';", cancellationToken);

        var businessColumns = await GetColumnsAsync(db, "BusinessSettings", cancellationToken);
        foreach (var sql in new[] { ("AiProvider", "ALTER TABLE BusinessSettings ADD COLUMN AiProvider TEXT NOT NULL DEFAULT 'auto';"), ("AiModel", "ALTER TABLE BusinessSettings ADD COLUMN AiModel TEXT NOT NULL DEFAULT '';"), ("AiBaseUrl", "ALTER TABLE BusinessSettings ADD COLUMN AiBaseUrl TEXT NOT NULL DEFAULT '';"), ("AiApiKeyEncrypted", "ALTER TABLE BusinessSettings ADD COLUMN AiApiKeyEncrypted TEXT NOT NULL DEFAULT '';") })
            if (!businessColumns.Contains(sql.Item1, StringComparer.OrdinalIgnoreCase)) await db.Database.ExecuteSqlRawAsync(sql.Item2, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Riders (Id INTEGER NOT NULL CONSTRAINT PK_Riders PRIMARY KEY AUTOINCREMENT, UserId INTEGER NOT NULL, VehicleType TEXT NOT NULL, VehicleNumber TEXT NOT NULL, Online INTEGER NOT NULL DEFAULT 0, Available INTEGER NOT NULL DEFAULT 1, LastSeenAtUtc TEXT NOT NULL);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Riders_UserId ON Riders (UserId);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS RiderLocations (Id INTEGER NOT NULL CONSTRAINT PK_RiderLocations PRIMARY KEY AUTOINCREMENT, RiderId INTEGER NOT NULL, Latitude REAL NOT NULL, Longitude REAL NOT NULL, AccuracyMeters REAL NULL, SpeedMetersPerSecond REAL NULL, HeadingDegrees REAL NULL, RecordedAtUtc TEXT NOT NULL);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_RiderLocations_RiderId_RecordedAtUtc ON RiderLocations (RiderId, RecordedAtUtc);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS DeliveryTrackings (Id INTEGER NOT NULL CONSTRAINT PK_DeliveryTrackings PRIMARY KEY AUTOINCREMENT, OrderId INTEGER NOT NULL, RiderId INTEGER NULL, TrackingCode TEXT NOT NULL, Status TEXT NOT NULL, DeliveryAddress TEXT NOT NULL, DestinationLatitude REAL NULL, DestinationLongitude REAL NULL, UpdatedAtUtc TEXT NOT NULL);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_DeliveryTrackings_OrderId ON DeliveryTrackings (OrderId);", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_DeliveryTrackings_TrackingCode ON DeliveryTrackings (TrackingCode);", cancellationToken);
    }

    private static async Task<HashSet<string>> GetColumnsAsync(CoreDbContext db, string tableName, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(1));
        return result;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}