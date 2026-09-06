using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;

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
            var next = DateTime.UtcNow;
            return Results.Ok(new { serverTimeUtc = next, since = watermark, products, categories, promotions, tables });
        }).RequireAuthorization();

        app.MapPost("/api/sync/push-orders", async (List<CreateOrderRequest> orders, HttpContext context, CoreDbContext db, CancellationToken ct) =>
        {
            if (orders.Count > 100) return Results.BadRequest("Maximum 100 orders per sync batch.");
            var results = new List<object>();
            foreach (var input in orders)
            {
                if (input.Items is null || input.Items.Count == 0) { results.Add(new { success = false, error = "Order has no items." }); continue; }
                var ids = input.Items.Select(x => x.ProductId).Distinct().ToList();
                var products = await db.Products.Where(x => ids.Contains(x.Id) && x.Available).ToDictionaryAsync(x => x.Id, ct);
                if (products.Count != ids.Count) { results.Add(new { success = false, error = "One or more products are unavailable." }); continue; }
                var order = new PosOrder { CustomerId = input.CustomerId, TableId = input.TableId, CreatedByUsername = context.User.Identity?.Name ?? "sync", OrderType = input.OrderType, Notes = input.Notes };
                foreach (var item in input.Items)
                {
                    if (item.Quantity <= 0 || !products.TryGetValue(item.ProductId, out var product)) { order = null!; break; }
                    order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price, Quantity = item.Quantity, Notes = item.Notes, LineTotal = product.Price * item.Quantity });
                }
                if (order is null) { results.Add(new { success = false, error = "Invalid quantity or product." }); continue; }
                order.Subtotal = order.Items.Sum(x => x.LineTotal);
                var setting = await db.BusinessSettings.AsNoTracking().FirstOrDefaultAsync(ct);
                var taxRate = setting?.TaxRate ?? 0m;
                order.Tax = Math.Round(order.Subtotal * taxRate / 100m, 2);
                order.Total = order.Subtotal + order.Tax;
                db.Orders.Add(order); await db.SaveChangesAsync(ct);
                results.Add(new { success = true, orderId = order.Id });
            }
            return Results.Ok(new { results });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter", "Waiter"));
    }
}
