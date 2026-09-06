using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class TableEndpoints
{
    private static readonly string[] AllowedStatuses = ["Available", "Occupied", "Reserved"];

    public static void MapTableEndpoints(this WebApplication app)
    {
        app.MapGet("/api/tables", async (CoreDbContext db, bool? active) =>
        {
            var q = db.Tables.AsNoTracking().AsQueryable();
            if (active != false) q = q.Where(x => x.Active);
            return Results.Ok(await q.OrderBy(x => x.Name).ToListAsync());
        }).RequireAuthorization();

        app.MapPost("/api/tables", async (TableRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest("Table name is required.");
            if (input.Capacity < 1 || input.Capacity > 100) return Results.BadRequest("Table capacity must be between 1 and 100.");
            if (await db.Tables.AnyAsync(x => x.Name == input.Name.Trim())) return Results.Conflict("A table with that name already exists.");
            var table = new PosTable { Name = input.Name.Trim(), Capacity = input.Capacity, Active = input.Active };
            db.Tables.Add(table); await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Created", "Table", table.Id.ToString(), table.Name);
            return Results.Created($"/api/tables/{table.Id}", table);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPut("/api/tables/{id:int}", async (int id, TableRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name) || input.Capacity < 1 || input.Capacity > 100) return Results.BadRequest("Valid table name and capacity are required.");
            var table = await db.Tables.FindAsync(id); if (table is null) return Results.NotFound();
            if (await db.Tables.AnyAsync(x => x.Id != id && x.Name == input.Name.Trim())) return Results.Conflict("A table with that name already exists.");
            table.Name = input.Name.Trim(); table.Capacity = input.Capacity; table.Active = input.Active; table.UpdatedAtUtc = DateTime.UtcNow;
            if (!table.Active) table.Status = "Available";
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Updated", "Table", table.Id.ToString(), table.Name);
            return Results.Ok(table);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPatch("/api/tables/{id:int}/status", async (int id, TableStatusRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (!AllowedStatuses.Contains(input.Status, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Invalid table status.");
            var table = await db.Tables.FindAsync(id); if (table is null) return Results.NotFound();
            if (!table.Active && !input.Status.Equals("Available", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Inactive tables cannot be reserved or occupied.");
            table.Status = AllowedStatuses.First(x => x.Equals(input.Status, StringComparison.OrdinalIgnoreCase)); table.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "StatusChanged", "Table", table.Id.ToString(), table.Status);
            return Results.Ok(table);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter", "Waiter"));
    }
}
