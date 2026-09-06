using System.Text.Json;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

/// <summary>
/// Reliable notification delivery queue. Notifications are delivered immediately to
/// authenticated SignalR clients and remain queued/retryable when no live client exists.
/// Native FCM/APNs delivery can be layered on the same queue later without changing callers.
/// </summary>
public sealed class PushNotificationHostedService(
    IServiceScopeFactory scopes,
    IHubContext<PosHub> hub,
    ILogger<PushNotificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatch(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Notification worker failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    private async Task ProcessBatch(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var now = DateTime.UtcNow;
        var items = await db.Notifications
            .Where(x => x.Status == "Queued" || (x.Status == "Retry" && (x.LastAttemptAtUtc == null || x.LastAttemptAtUtc < now.AddSeconds(-10))))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(20)
            .ToListAsync(ct);
        if (items.Count == 0) return;

        foreach (var item in items)
        {
            item.Attempts++;
            item.LastAttemptAtUtc = now;
            var delivered = false;
            try
            {
                var payload = new
                {
                    id = item.Id,
                    title = item.Title,
                    body = item.Body,
                    data = JsonSerializer.Deserialize<Dictionary<string, string>>(item.DataJson) ?? new()
                };
                await hub.Clients.Group($"user:{item.Username}").SendAsync("notification.created", payload, ct);
                delivered = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                item.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            }

            var hasDevice = await db.PushDevices.AnyAsync(x => x.Username == item.Username && x.Active, ct);
            if (delivered)
            {
                item.Status = "DeliveredRealtime";
                item.SentAtUtc = now;
                item.LastError = hasDevice
                    ? "Delivered to the live SignalR client; native push delivery requires a configured FCM/APNs adapter."
                    : "Delivered to the live SignalR client; no native push device is currently registered.";
            }
            else
            {
                item.Status = hasDevice ? "Retry" : "NoDevice";
                if (!hasDevice && string.IsNullOrWhiteSpace(item.LastError)) item.LastError = "No active push device is registered and no live SignalR client received the notification.";
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
