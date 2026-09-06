using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class InventoryEndpoints
{
    private static readonly string[] ManageRoles = ["Owner", "Manager", "Admin"];

    public static void MapInventoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/inventory", async (CoreDbContext db) =>
        {
            var rows = await db.Products.AsNoTracking().Where(x => x.Available).Join(db.ProductInventories.AsNoTracking(), p => p.Id, i => i.ProductId, (p, i) => new { p.Id, p.Name, p.Barcode, i.QuantityOnHand, i.ReorderLevel, i.TrackInventory, i.UpdatedAtUtc }).OrderBy(x => x.Name).ToListAsync();
            return Results.Ok(rows);
        }).RequireAuthorization(p => p.RequireRole(ManageRoles));

        app.MapGet("/api/inventory/low-stock", async (CoreDbContext db) =>
        {
            var rows = await db.Products.AsNoTracking().Join(db.ProductInventories.AsNoTracking().Where(i => i.TrackInventory && i.QuantityOnHand <= i.ReorderLevel), p => p.Id, i => i.ProductId, (p, i) => new { p.Id, p.Name, p.Barcode, i.QuantityOnHand, i.ReorderLevel }).OrderBy(x => x.QuantityOnHand).ToListAsync();
            return Results.Ok(rows);
        }).RequireAuthorization(p => p.RequireRole(ManageRoles));

        app.MapPut("/api/products/{id:int}/inventory", async (int id, InventoryUpdateRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            if (input.QuantityOnHand < 0 || input.QuantityOnHand > 1000000000m) return Results.BadRequest("Quantity on hand is invalid.");
            if (input.ReorderLevel < 0 || input.ReorderLevel > 1000000000m) return Results.BadRequest("Reorder level is invalid.");
            var product = await db.Products.FindAsync(id);
            if (product is null) return Results.NotFound();
            var inventory = await db.ProductInventories.SingleOrDefaultAsync(x => x.ProductId == id);
            var old = inventory?.QuantityOnHand ?? 0m;
            inventory ??= new ProductInventory { ProductId = id };
            inventory.QuantityOnHand = input.QuantityOnHand;
            inventory.ReorderLevel = input.ReorderLevel;
            inventory.TrackInventory = input.TrackInventory;
            inventory.UpdatedAtUtc = DateTime.UtcNow;
            if (inventory.Id == 0) db.ProductInventories.Add(inventory);
            var delta = input.QuantityOnHand - old;
            if (delta != 0) db.StockMovements.Add(new StockMovement { ProductId = id, QuantityChange = delta, BalanceAfter = input.QuantityOnHand, Type = "Adjustment", Reason = input.Reason?.Trim() ?? "", Username = user.Identity?.Name ?? "unknown" });
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("inventory.updated", new { productId = id, quantityOnHand = inventory.QuantityOnHand, trackInventory = inventory.TrackInventory });
            return Results.Ok(inventory);
        }).RequireAuthorization(p => p.RequireRole(ManageRoles));

        app.MapPost("/api/products/{id:int}/inventory/adjust", async (int id, StockAdjustmentRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            if (input.QuantityChange == 0 || Math.Abs(input.QuantityChange) > 1000000000m) return Results.BadRequest("Stock adjustment is invalid.");
            var product = await db.Products.FindAsync(id);
            if (product is null) return Results.NotFound();
            var inventory = await db.ProductInventories.SingleOrDefaultAsync(x => x.ProductId == id);
            if (inventory is null || !inventory.TrackInventory) return Results.BadRequest("Inventory tracking is not enabled for this product.");
            var next = inventory.QuantityOnHand + input.QuantityChange;
            if (next < 0) return Results.BadRequest("Stock cannot become negative.");
            inventory.QuantityOnHand = next;
            inventory.UpdatedAtUtc = DateTime.UtcNow;
            db.StockMovements.Add(new StockMovement { ProductId = id, QuantityChange = input.QuantityChange, BalanceAfter = next, Type = "Adjustment", Reason = input.Reason?.Trim() ?? "", Username = user.Identity?.Name ?? "unknown" });
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("inventory.updated", new { productId = id, quantityOnHand = next, trackInventory = true });
            return Results.Ok(inventory);
        }).RequireAuthorization(p => p.RequireRole(ManageRoles));

        app.MapGet("/api/products/{id:int}/inventory/movements", async (int id, CoreDbContext db) => Results.Ok(await db.StockMovements.AsNoTracking().Where(x => x.ProductId == id).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync())).RequireAuthorization(p => p.RequireRole(ManageRoles));
    }
}
