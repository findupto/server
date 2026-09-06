using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class AuditEndpoints
{
    public static async Task WriteAsync(CoreDbContext db, ClaimsPrincipal user, string action, string entityType, string entityId, string details = "")
    {
        db.AuditLogs.Add(new AuditLog
        {
            Username = user.Identity?.Name ?? "unknown",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
        await db.SaveChangesAsync();
    }

    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/audit-logs", async (CoreDbContext db, int? take) =>
        {
            var limit = Math.Clamp(take ?? 100, 1, 500);
            return Results.Ok(await db.AuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(limit).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}
