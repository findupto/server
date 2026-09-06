using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class PurchasingEndpoints
{
    private static readonly string[] ManageRoles = ["Owner", "Manager", "Admin"];
    private static readonly string[] Statuses = ["Draft", "Ordered", "PartiallyReceived", "Received", "Cancelled"];

    public static void MapPurchasingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/suppliers", async (CoreDbContext db) => Results.Ok(await db.Suppliers.AsNoTracking().OrderBy(x => x.Name).ToListAsync())).RequireAuthorization(p => p.RequireRole(ManageRoles));
        app.MapPost("/api/suppliers", async (SupplierRequest input, CoreDbContext db) => { if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 160) return Results.BadRequest("Supplier name is required and must be 160 characters or fewer."); var supplier = new Supplier { Name = input.Name.Trim(), Phone = input.Phone?.Trim() ?? "", Email = input.Email?.Trim() ?? "", Address = input.Address?.Trim() ?? "", Notes = input.Notes?.Trim() ?? "", Active = input.Active }; db.Suppliers.Add(supplier); await db.SaveChangesAsync(); return Results.Created($"/api/suppliers/{supplier.Id}", supplier); }).RequireAuthorization(p => p.RequireRole(ManageRoles));
        app.MapPut("/api/suppliers/{id:int}", async (int id, SupplierRequest input, CoreDbContext db) => { if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest("Supplier name is required."); var supplier = await db.Suppliers.FindAsync(id); if (supplier is null) return Results.NotFound(); supplier.Name = input.Name.Trim(); supplier.Phone = input.Phone?.Trim() ?? ""; supplier.Email = input.Email?.Trim() ?? ""; supplier.Address = input.Address?.Trim() ?? ""; supplier.Notes = input.Notes?.Trim() ?? ""; supplier.Active = input.Active; await db.SaveChangesAsync(); return Results.Ok(supplier); }).RequireAuthorization(p => p.RequireRole(ManageRoles));
        app.MapGet("/api/purchase-orders", async (CoreDbContext db, string? status) => { var q = db.PurchaseOrders.AsNoTracking().Include(x => x.Items).AsQueryable(); if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status.Trim()); return Results.Ok(await q.OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync()); }).RequireAuthorization(p => p.RequireRole(ManageRoles));
        app.MapGet("/api/purchase-orders/{id:int}", async (int id, CoreDbContext db) => { var order = await db.PurchaseOrders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id); return order is null ? Results.NotFound() : Results.Ok(order); }).RequireAuthorization(p => p.RequireRole(ManageRoles));
        app.MapPost("/api/purchase-orders", async (PurchaseOrderRequest input, ClaimsPrincipal user, CoreDbContext db) => { if (!await db.Suppliers.AnyAsync(x => x.Id == input.SupplierId && x.Active)) return Results.BadRequest("Supplier not found or inactive."); if (input.Items is null || input.Items.Count == 0 || input.Items.Count > 200) return Results.BadRequest("Purchase order must contain 1 to 200 items."); var ids = input.Items.Select(x => x.ProductId).Distinct().ToList(); var products = await db.Products.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id); if (products.Count != ids.Count) return Results.BadRequest("One or more products were not found."); var order = new PurchaseOrder { SupplierId = input.SupplierId, CreatedByUsername = user.Identity?.Name ?? "unknown", Notes = input.Notes?.Trim() ?? "" }; foreach (var item in input.Items) { if (item.QuantityOrdered <= 0 || item.UnitCost < 0) return Results.BadRequest("Purchase quantities must be positive and unit costs cannot be negative."); var product = products[item.ProductId]; order.Items.Add(new PurchaseOrderItem { ProductId = product.Id, ProductName = product.Name, QuantityOrdered = item.QuantityOrdered, UnitCost = Math.Round(item.UnitCost, 2) }); } db.PurchaseOrders.Add(order); await db.SaveChangesAsync(); return Results.Created($"/api/purchase-orders/{order.Id}", order); }).RequireAuthorization(p => p.RequireRole(ManageRoles));
        app.MapPatch("/api/purchase-orders/{id:int}/status", async (int id, UpdateOrderStatusRequest input, CoreDbContext db) => { var next = Statuses.FirstOrDefault(x => x.Equals(input.Status, StringComparison.OrdinalIgnoreCase)); if (next is null) return Results.BadRequest("Invalid purchase order status."); var order = await db.PurchaseOrders.FindAsync(id); if (order is null) return Results.NotFound(); if (order.Status is "Received" or "Cancelled") return Results.Conflict("A received or cancelled purchase order cannot change status."); if (next is "Received" or "PartiallyReceived") return Results.BadRequest("Use the receive endpoint to record stock received."); order.Status = next; order.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(); return Results.Ok(order); }).RequireAuthorization(p => p.RequireRole(ManageRoles));
        app.MapPost("/api/purchase-orders/{id:int}/receive", async (int id, PurchaseReceiveRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            if (input.Items is null || input.Items.Count == 0) return Results.BadRequest("At least one received item is required.");
            var order = await db.PurchaseOrders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id); if (order is null) return Results.NotFound(); if (order.Status is "Cancelled" or "Received") return Results.Conflict("This purchase order cannot receive more stock.");
            var grouped = input.Items.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity)); if (grouped.Values.Any(x => x <= 0)) return Results.BadRequest("Received quantities must be greater than zero.");
            await using var tx = await db.Database.BeginTransactionAsync(); var changed = new List<(int ProductId, decimal Balance)>();
            foreach (var (productId, quantity) in grouped)
            {
                var item = order.Items.SingleOrDefault(x => x.ProductId == productId); if (item is null) { await tx.RollbackAsync(); return Results.BadRequest($"Product {productId} is not part of this purchase order."); }
                if (item.QuantityReceived + quantity > item.QuantityOrdered) { await tx.RollbackAsync(); return Results.BadRequest($"Received quantity exceeds ordered quantity for {item.ProductName}."); }
                var inventory = await db.ProductInventories.SingleOrDefaultAsync(x => x.ProductId == productId); if (inventory is null) { inventory = new ProductInventory { ProductId = productId, TrackInventory = true }; db.ProductInventories.Add(inventory); }
                var oldQuantity = inventory.QuantityOnHand; var oldCost = inventory.AverageCost; var unitCost = item.UnitCost; var newQuantity = oldQuantity + quantity;
                inventory.AverageCost = newQuantity == 0 ? unitCost : Math.Round(((oldQuantity * oldCost) + (quantity * unitCost)) / newQuantity, 4); inventory.QuantityOnHand = newQuantity; inventory.TrackInventory = true; inventory.UpdatedAtUtc = DateTime.UtcNow; item.QuantityReceived += quantity;
                db.StockMovements.Add(new StockMovement { ProductId = productId, QuantityChange = quantity, BalanceAfter = newQuantity, UnitCost = unitCost, Type = "Purchase", Reason = $"PurchaseOrder {order.Id}" + (string.IsNullOrWhiteSpace(input.Notes) ? "" : $"; {input.Notes.Trim()}"), Username = user.Identity?.Name ?? "unknown", CreatedAtUtc = DateTime.UtcNow }); changed.Add((productId, newQuantity));
            }
            order.Status = order.Items.All(x => x.QuantityReceived >= x.QuantityOrdered) ? "Received" : "PartiallyReceived"; order.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync();
            foreach (var change in changed) { var stock = await db.ProductInventories.AsNoTracking().SingleAsync(x => x.ProductId == change.ProductId); await hub.Clients.All.SendAsync("inventory.updated", new { productId = change.ProductId, quantityOnHand = stock.QuantityOnHand, averageCost = stock.AverageCost, reorderLevel = stock.ReorderLevel, trackInventory = stock.TrackInventory, updatedAtUtc = stock.UpdatedAtUtc }); }
            return Results.Ok(order);
        }).RequireAuthorization(p => p.RequireRole(ManageRoles));
    }
}
