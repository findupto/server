using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this WebApplication app)
    {
        app.MapGet("/api/sync/conflicts", async (CoreDbContext db, CancellationToken ct) => Results.Ok(await db.SyncConflicts.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct)))
            .RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}