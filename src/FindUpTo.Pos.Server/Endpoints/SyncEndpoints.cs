using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this WebApplication app)
    {
        app.MapGet("/api/sync/pull", async (DateTime? since, CoreDbContext db, CancellationToken ct) =>
        {
            var watermark = since?.ToUniversalTime() ?? DateTime.MinValue;
            var products = await db.Products.AsNoTracking().Where(x => x.UpdatedAtUtc > watermark).Select(x => new { x.Id, x.CategoryId, x.Name, x.Description, x.Price, x.ImageUrl, x.Barcode, x.Available, x.UpdatedAtUtc }).ToListAsync(ct);
            var categories = await db.Categories.AsNoTracking().Select(x => new { x.Id, x.Name, x.Active, x.SortOrder }).ToListAsync(ct);
            var promotions = await db.Promotions.AsNoTracking().Where(x => x.UpdatedAtUtc > watermark).ToListAsync(ct);
            var tables = await db.Tables.AsNoTracking().Where(x => x.UpdatedAtUtc > watermark).Select(x => new { x.Id, x.Name, x.Capacity, x.Status, x.Active, x.UpdatedAtUtc }).ToListAsync(ct);
            return Results.Ok(new { serverTimeUtc = DateTime.UtcNow, since = watermark, products, categories, promotions, tables });
        }).RequireAuthorization();

        app.MapPost("/api/sync/push-orders", async (List<CreateOrderRequest> orders, HttpContext context, CoreDbContext db, CancellationToken ct) =>
        {
            if (orders.Count > 100) return Results.BadRequest("Maximum 100 orders per sync batch.");
            var results = new List<object>();
            foreach (var input in orders)
            {
                if (string.IsNullOrWhiteSpace(input.ClientOperationId) || input.ClientOperationId.Length > 128)
                { results.Add(new { success = false, error = "ClientOperationId is required for offline synchronization." }); continue; }
                var existing = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == input.ClientOperationId, ct);
                if (existing is not null)
                { results.Add(new { success = true, duplicate = true, orderId = existing.OrderId }); continue; }
                if (input.Items is null || input.Items.Count == 0) { results.Add(new { success = false, error = "Order has no items." }); continue; }
                var ids = input.Items.Select(x => x.ProductId).Distinct().ToList();
                var products = await db.Products.Where(x => ids.Contains(x.Id) && x.Available).ToDictionaryAsync(x => x.Id, ct);
                if (products.Count != ids.Count) { results.Add(new { success = false, error = "One or more products are unavailable." }); continue; }
                if (input.CustomerId.HasValue && !await db.Customers.AnyAsync(x => x.Id == input.CustomerId.Value, ct)) { results.Add(new { success = false, error = "Customer not found." }); continue; }
                var order = new PosOrder { CustomerId = input.CustomerId, TableId = input.TableId, CreatedByUsername = context.User.Identity?.Name ?? "sync", OrderType = string.IsNullOrWhiteSpace(input.OrderType) ? "Counter" : input.OrderType.Trim(), Notes = input.Notes.Trim() };
                foreach (var item in input.Items)
                {
                    if (item.Quantity <= 0 || !products.TryGetValue(item.ProductId, out var product)) { order = null!; break; }
                    order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price, Quantity = item.Quantity, Notes = item.Notes.Trim(), LineTotal = product.Price * item.Quantity });
                }
                if (order is null) { results.Add(new { success = false, error = "Invalid quantity or product." }); continue; }
                order.Subtotal = order.Items.Sum(x => x.LineTotal);
                var setting = await db.BusinessSettings.AsNoTracking().FirstOrDefaultAsync(ct);
                var taxRate = setting?.TaxPercent ?? 0m;
                order.Tax = Math.Round(order.Subtotal * taxRate / 100m, 2); order.Total = order.Subtotal + order.Tax;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    db.Orders.Add(order); await db.SaveChangesAsync(ct);
                    db.SyncOperations.Add(new SyncOperation { ClientOperationId = input.ClientOperationId, OrderId = order.Id, CreatedByUsername = context.User.Identity?.Name ?? "sync" });
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                    await AuditEndpoints.WriteAsync(db, context.User, "Synced", "Order", order.Id.ToString(), $"ClientOperationId={input.ClientOperationId}");
                    results.Add(new { success = true, duplicate = false, orderId = order.Id });
                }
                catch (DbUpdateException)
                {
                    await tx.RollbackAsync(ct);
                    var raced = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == input.ClientOperationId, ct);
                    results.Add(raced is null ? new { success = false, error = "Could not persist synchronized order." } : new { success = true, duplicate = true, orderId = raced.OrderId });
                }
            }
            return Results.Ok(new { results });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter", "Waiter"));
    }
}
