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
            var orders = await db.Orders.AsNoTracking().Where(x => x.UpdatedAtUtc > watermark).Select(x => new { x.Id, x.CustomerId, x.TableId, x.CreatedByUsername, x.OrderType, x.Status, x.Subtotal, x.Tax, x.Total, x.Notes, x.CreatedAtUtc, x.UpdatedAtUtc, Items = x.Items.Select(i => new { i.Id, i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.Notes, i.LineTotal }) }).ToListAsync(ct);
            var payments = await db.Payments.AsNoTracking().Where(x => x.CreatedAtUtc > watermark).Select(x => new { x.Id, x.PosOrderId, x.AmountTendered, x.AmountPaid, x.ChangeAmount, x.Method, x.Status, x.Reference, x.CollectedByUsername, x.CreatedAtUtc }).ToListAsync(ct);
            var customers = await db.Customers.AsNoTracking().Where(x => x.CreatedAtUtc > watermark).Select(x => new { x.Id, x.Name, x.Phone, x.Address, x.Notes, x.CreatedAtUtc }).ToListAsync(ct);
            return Results.Ok(new { serverTimeUtc = DateTime.UtcNow, since = watermark, products, categories, promotions, tables, customers, orders, payments });
        }).RequireAuthorization();

        app.MapGet("/api/sync/conflicts", async (CoreDbContext db, CancellationToken ct) => Results.Ok(await db.SyncConflicts.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct)))
            .RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPost("/api/sync/push-orders", async (List<CreateOrderRequest> orders, HttpContext context, CoreDbContext db, CancellationToken ct) =>
        {
            if (orders.Count > 100) return Results.BadRequest("Maximum 100 orders per sync batch.");
            var results = new List<object>();
            var username = context.User.Identity?.Name ?? "sync";
            async Task RecordConflict(string operationId, string entityId, string reason, string details)
            {
                db.SyncConflicts.Add(new SyncConflict { ClientOperationId = operationId, EntityType = "Order", EntityId = entityId, Reason = reason, Details = details, Username = username });
                await db.SaveChangesAsync(ct);
            }
            foreach (var input in orders)
            {
                if (string.IsNullOrWhiteSpace(input.ClientOperationId) || input.ClientOperationId.Length > 128) { results.Add(new { success = false, error = "ClientOperationId is required for offline synchronization." }); continue; }
                var existing = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == input.ClientOperationId, ct);
                if (existing is not null)
                {
                    var paid = await db.Payments.AsNoTracking().AnyAsync(x => x.PosOrderId == existing.OrderId && x.Status == "Paid", ct);
                    if (!paid && !string.IsNullOrWhiteSpace(input.PaymentMethod) && input.AmountTendered.HasValue)
                    {
                        var existingOrder = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existing.OrderId, ct);
                        if (existingOrder is not null && input.AmountTendered.Value >= existingOrder.Total)
                        {
                            db.Payments.Add(new Payment { PosOrderId = existingOrder.Id, AmountTendered = input.AmountTendered.Value, AmountPaid = existingOrder.Total, ChangeAmount = input.PaymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase) ? Math.Round(input.AmountTendered.Value - existingOrder.Total, 2) : 0m, Method = input.PaymentMethod.Trim(), Reference = input.PaymentReference.Trim(), CollectedByUsername = username });
                            await db.SaveChangesAsync(ct);
                        }
                    }
                    results.Add(new { success = true, duplicate = true, orderId = existing.OrderId, paid = await db.Payments.AnyAsync(x => x.PosOrderId == existing.OrderId && x.Status == "Paid", ct) });
                    continue;
                }
                if (input.Items is null || input.Items.Count == 0) { await RecordConflict(input.ClientOperationId, "", "EmptyOrder", "Order has no items."); results.Add(new { success = false, conflict = true, error = "Order has no items." }); continue; }
                var ids = input.Items.Select(x => x.ProductId).Distinct().ToList();
                var products = await db.Products.Where(x => ids.Contains(x.Id) && x.Available).ToDictionaryAsync(x => x.Id, ct);
                if (products.Count != ids.Count) { await RecordConflict(input.ClientOperationId, "", "ProductUnavailable", "One or more products are unavailable."); results.Add(new { success = false, conflict = true, error = "One or more products are unavailable." }); continue; }
                if (input.CustomerId.HasValue && !await db.Customers.AnyAsync(x => x.Id == input.CustomerId.Value, ct)) { await RecordConflict(input.ClientOperationId, input.CustomerId.Value.ToString(), "CustomerMissing", "Customer not found."); results.Add(new { success = false, conflict = true, error = "Customer not found." }); continue; }
                if (input.TableId.HasValue && !await db.Tables.AnyAsync(x => x.Id == input.TableId.Value && x.Active, ct)) { await RecordConflict(input.ClientOperationId, input.TableId.Value.ToString(), "TableUnavailable", "Table not found or inactive."); results.Add(new { success = false, conflict = true, error = "Table not found or inactive." }); continue; }
                var order = new PosOrder { CustomerId = input.CustomerId, TableId = input.TableId, CreatedByUsername = username, OrderType = string.IsNullOrWhiteSpace(input.OrderType) ? "Counter" : input.OrderType.Trim(), Notes = input.Notes.Trim() };
                foreach (var item in input.Items)
                {
                    if (item.Quantity <= 0 || !products.TryGetValue(item.ProductId, out var product)) { order = null!; break; }
                    order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price, Quantity = item.Quantity, Notes = item.Notes.Trim(), LineTotal = product.Price * item.Quantity });
                }
                if (order is null) { await RecordConflict(input.ClientOperationId, "", "InvalidLine", "Invalid quantity or product."); results.Add(new { success = false, conflict = true, error = "Invalid quantity or product." }); continue; }
                order.Subtotal = order.Items.Sum(x => x.LineTotal);
                var setting = await db.BusinessSettings.AsNoTracking().FirstOrDefaultAsync(ct);
                order.Tax = Math.Round(order.Subtotal * (setting?.TaxPercent ?? 0m) / 100m, 2); order.Total = order.Subtotal + order.Tax;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    db.Orders.Add(order); await db.SaveChangesAsync(ct);
                    if (!string.IsNullOrWhiteSpace(input.PaymentMethod) && input.AmountTendered.HasValue && input.AmountTendered.Value >= order.Total)
                        db.Payments.Add(new Payment { PosOrderId = order.Id, AmountTendered = input.AmountTendered.Value, AmountPaid = order.Total, ChangeAmount = input.PaymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase) ? Math.Round(input.AmountTendered.Value - order.Total, 2) : 0m, Method = input.PaymentMethod.Trim(), Reference = input.PaymentReference.Trim(), CollectedByUsername = username });
                    db.SyncOperations.Add(new SyncOperation { ClientOperationId = input.ClientOperationId, OrderId = order.Id, CreatedByUsername = username });
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                    await AuditEndpoints.WriteAsync(db, context.User, "Synced", "Order", order.Id.ToString(), $"ClientOperationId={input.ClientOperationId}");
                    results.Add(new { success = true, duplicate = false, orderId = order.Id, paid = input.PaymentMethod is not null && input.AmountTendered >= order.Total });
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
