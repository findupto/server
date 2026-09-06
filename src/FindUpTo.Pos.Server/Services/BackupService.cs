using FindUpTo.Pos.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public static class BackupService
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
    public static string GetDatabasePath(CoreDbContext db) { var path = db.Database.GetDbConnection().DataSource; if (string.IsNullOrWhiteSpace(path) || path == ":memory:") throw new InvalidOperationException("A file-backed SQLite database is required."); return Path.GetFullPath(path); }
    public static async Task<FileInfo> CreateBackupAsync(CoreDbContext db, string contentRootPath, CancellationToken cancellationToken = default)
    {
        var databasePath = GetDatabasePath(db); if (!File.Exists(databasePath)) throw new FileNotFoundException("Database file not found.", databasePath);
        var directory = Path.Combine(contentRootPath, "backups"); Directory.CreateDirectory(directory);
        var backupPath = Path.Combine(directory, $"pos-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.db");
        await db.Database.ExecuteSqlInterpolatedAsync($"VACUUM INTO {backupPath}", cancellationToken); return new FileInfo(backupPath);
    }
    public static async Task RestoreAsync(string backupPath, CoreDbContext db, CancellationToken cancellationToken = default)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = GetDatabasePath(db) }.ToString());
        await source.OpenAsync(cancellationToken); await destination.OpenAsync(cancellationToken); source.BackupDatabase(destination);
    }
    public static void PruneBackups(string contentRootPath, int retentionDays)
    {
        var directory = Path.Combine(contentRootPath, "backups"); if (!Directory.Exists(directory)) return;
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
        foreach (var file in Directory.EnumerateFiles(directory, "pos-*.db")) { var info = new FileInfo(file); if (info.CreationTimeUtc < cutoff) info.Delete(); }
    }
}

public sealed class AutomaticBackupHostedService(IServiceScopeFactory scopeFactory, IWebHostEnvironment environment, IConfiguration configuration, ILogger<AutomaticBackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = Math.Clamp(configuration.GetValue("Backup:IntervalHours", 24), 1, 168); var retentionDays = Math.Clamp(configuration.GetValue("Backup:RetentionDays", 30), 1, 3650);
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BackupService.Gate.WaitAsync(stoppingToken);
                try { using var scope = scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>(); var file = await BackupService.CreateBackupAsync(db, environment.ContentRootPath, stoppingToken); BackupService.PruneBackups(environment.ContentRootPath, retentionDays); logger.LogInformation("Automatic POS backup created: {File}", file.FullName); }
                finally { BackupService.Gate.Release(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Automatic POS backup failed."); }
            await Task.Delay(TimeSpan.FromHours(hours), stoppingToken);
        }
    }
}
