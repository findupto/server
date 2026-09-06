using System.Security.Claims;
using System.Text.Json;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/notifications/devices", async (PushDevice input, HttpContext context, CoreDbContext db, CancellationToken ct) =>
        {
            var username = context.User.Identity?.Name ?? "";
            if (string.IsNullOrWhiteSpace(input.Token) || input.Token.Length > 4096) return Results.BadRequest("A valid push token is required.");
            var platform = input.Platform.Trim().ToLowerInvariant();
            if (platform is not ("android" or "ios" or "windows")) return Results.BadRequest("Platform must be android, ios, or windows.");
            var token = input.Token.Trim();
            var device = await db.PushDevices.SingleOrDefaultAsync(x => x.Username == username && x.Token == token, ct);
            if (device is null) db.PushDevices.Add(new PushDevice { Username = username, Platform = platform, Token = token });
            else { device.Platform = platform; device.Active = true; device.UpdatedAtUtc = DateTime.UtcNow; }
            await db.SaveChangesAsync(ct); return Results.Ok(new { registered = true });
        }).RequireAuthorization();

        app.MapDelete("/api/notifications/devices", async (string token, HttpContext context, CoreDbContext db, CancellationToken ct) =>
        {
            var username = context.User.Identity?.Name ?? "";
            var device = await db.PushDevices.SingleOrDefaultAsync(x => x.Username == username && x.Token == token, ct);
            if (device is null) return Results.NotFound(); device.Active = false; device.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return Results.Ok();
        }).RequireAuthorization();

        app.MapGet("/api/notifications", async (HttpContext context, CoreDbContext db, CancellationToken ct) =>
        {
            var username = context.User.Identity?.Name ?? "";
            return Results.Ok(await db.Notifications.AsNoTracking().Where(x => x.Username == username).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct));
        }).RequireAuthorization();

        app.MapPost("/api/notifications/send", async (NotificationRequest input, ClaimsPrincipal user, CoreDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Body)) return Results.BadRequest("Username, title and body are required.");
            if (input.Title.Length > 160 || input.Body.Length > 1000) return Results.BadRequest("Notification text is too long.");
            var username = input.Username.Trim();
            if (!await db.Users.AsNoTracking().AnyAsync(x => x.Username == username && x.Active, ct)) return Results.NotFound("User not found.");
            var data = input.Data is null ? "{}" : JsonSerializer.Serialize(input.Data);
            if (data.Length > 4000) return Results.BadRequest("Notification data is too large.");
            var item = new NotificationMessage { Username = username, Title = input.Title.Trim(), Body = input.Body.Trim(), DataJson = data };
            db.Notifications.Add(item); await db.SaveChangesAsync(ct);
            await AuditEndpoints.WriteAsync(db, user, "Queued", "Notification", item.Id.ToString(), $"To={item.Username}");
            return Results.Accepted($"/api/notifications/{item.Id}", new { item.Id, item.Status });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}

public sealed record NotificationRequest(string Username, string Title, string Body, Dictionary<string, string>? Data);
