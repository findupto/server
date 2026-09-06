using System.Text.Json;
using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

/// <summary>
/// Reliable notification delivery queue. Provider credentials are intentionally externalized;
/// this worker persists retries until an FCM/APNs provider adapter is configured.
/// </summary>
public sealed class PushNotificationHostedService(IServiceScopeFactory scopes, ILogger<PushNotificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatch(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Notification worker failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    private async Task ProcessBatch(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var now = DateTime.UtcNow;
        var items = await db.Notifications
            .Where(x => x.Status == "Queued" || (x.Status == "Retry" && x.LastAttemptAtUtc < now.AddSeconds(-30)))
            .OrderBy(x => x.CreatedAtUtc).Take(20).ToListAsync(ct);
        if (items.Count == 0) return;

        foreach (var item in items)
        {
            var devices = await db.PushDevices.Where(x => x.Username == item.Username && x.Active).ToListAsync(ct);
            item.Attempts++;
            item.LastAttemptAtUtc = now;
            if (devices.Count == 0)
            {
                item.Status = "NoDevice";
                item.LastError = "No active push device is registered.";
                continue;
            }

            // Provider adapters can be enabled by configuration without changing the queue contract.
            // Never claim delivery until a provider confirms it.
            item.Status = "Retry";
            item.LastError = "No push provider is configured.";
            _ = JsonSerializer.Deserialize<Dictionary<string, string>>(item.DataJson);
        }
        await db.SaveChangesAsync(ct);
    }
}
