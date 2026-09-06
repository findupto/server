using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class SyncEndpoints
{
    private static readonly string[] PaymentMethods = ["Cash", "Card", "Online"];
    private const int MaxItemsPerOrder = 100;

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
                if (string.IsNullOrWhiteSpace(input.ClientOperationId) || input.ClientOperationId.Length > 128)
                { results.Add(new { success = false, error = "ClientOperationId is required for offline synchronization." }); continue; }
                if (input.Items is null || input.Items.Count == 0 || input.Items.Count > MaxItemsPerOrder)
                { await RecordConflict(input.ClientOperationId, "", "InvalidItems", "Order must contain between 1 and 100 items."); results.Add(new { success = false, conflict = true, error = "Invalid order items." }); continue; }

                var existing = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == input.ClientOperationId, ct);
                if (existing is not null)
                {
                    var paid = await db.Payments.AsNoTracking().AnyAsync(x => x.PosOrderId == existing.OrderId && x.Status == "Paid", ct);
                    if (!paid && HasPayment(input))
                    {
                        var paymentResult = await TryCreatePaymentAsync(db, existing.OrderId, input, username, ct);
                        if (!paymentResult.Success)
                            results.Add(new { success = true, duplicate = true, orderId = existing.OrderId, paid = false, paymentError = paymentResult.Error });
                        else
                            results.Add(new { success = true, duplicate = true, orderId = existing.OrderId, paid = true });
                    }
                    else results.Add(new { success = true, duplicate = true, orderId = existing.OrderId, paid });
                    continue;
                }

                var ids = input.Items.Select(x => x.ProductId).Distinct().ToList();
                var products = await db.Products.Where(x => ids.Contains(x.Id) && x.Available).ToDictionaryAsync(x => x.Id, ct);
                if (products.Count != ids.Count) { await RecordConflict(input.ClientOperationId, "", "ProductUnavailable", "One or more products are unavailable."); results.Add(new { success = false, conflict = true, error = "One or more products are unavailable." }); continue; }
                if (input.CustomerId.HasValue && !await db.Customers.AnyAsync(x => x.Id == input.CustomerId.Value, ct)) { await RecordConflict(input.ClientOperationId, input.CustomerId.Value.ToString(), "CustomerMissing", "Customer not found."); results.Add(new { success = false, conflict = true, error = "Customer not found." }); continue; }
                if (input.TableId.HasValue && !await db.Tables.AnyAsync(x => x.Id == input.TableId.Value && x.Active, ct)) { await RecordConflict(input.ClientOperationId, input.TableId.Value.ToString(), "TableUnavailable", "Table not found or inactive."); results.Add(new { success = false, conflict = true, error = "Table not found or inactive." }); continue; }

                var orderType = string.IsNullOrWhiteSpace(input.OrderType) ? "Counter" : input.OrderType.Trim();
                if (!new[] { "Counter", "Dine In", "Takeaway", "Delivery" }.Contains(orderType, StringComparer.OrdinalIgnoreCase))
                { await RecordConflict(input.ClientOperationId, "", "InvalidOrderType", "Unsupported order type."); results.Add(new { success = false, conflict = true, error = "Unsupported order type." }); continue; }
                if (orderType.Equals("Dine In", StringComparison.OrdinalIgnoreCase) && !input.TableId.HasValue)
                { await RecordConflict(input.ClientOperationId, "", "TableRequired", "Dine In orders require a table."); results.Add(new { success = false, conflict = true, error = "Dine In orders require a table." }); continue; }

                var order = new PosOrder { CustomerId = input.CustomerId, TableId = input.TableId, CreatedByUsername = username, OrderType = orderType, Notes = (input.Notes ?? string.Empty).Trim() };
                foreach (var item in input.Items)
                {
                    if (item.Quantity <= 0 || item.Quantity > 1000 || !products.TryGetValue(item.ProductId, out var product)) { order = null!; break; }
                    order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price, Quantity = item.Quantity, Notes = (item.Notes ?? string.Empty).Trim(), LineTotal = Math.Round(product.Price * item.Quantity, 2) });
                }
                if (order is null) { await RecordConflict(input.ClientOperationId, "", "InvalidLine", "Invalid quantity or product."); results.Add(new { success = false, conflict = true, error = "Invalid quantity or product." }); continue; }
                order.Subtotal = Math.Round(order.Items.Sum(x => x.LineTotal), 2);
                var setting = await db.BusinessSettings.AsNoTracking().FirstOrDefaultAsync(ct);
                order.Tax = Math.Round(order.Subtotal * (setting?.TaxPercent ?? 0m) / 100m, 2); order.Total = Math.Round(order.Subtotal + order.Tax, 2);

                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    db.Orders.Add(order); await db.SaveChangesAsync(ct);
                    if (HasPayment(input))
                    {
                        var paymentResult = await AddPaymentAsync(db, order.Id, order.Total, input, username, ct);
                        if (!paymentResult.Success) { await tx.RollbackAsync(ct); await RecordConflict(input.ClientOperationId, order.Id.ToString(), "InvalidPayment", paymentResult.Error!); results.Add(new { success = false, conflict = true, error = paymentResult.Error }); continue; }
                    }
                    db.SyncOperations.Add(new SyncOperation { ClientOperationId = input.ClientOperationId, OrderId = order.Id, CreatedByUsername = username });
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                    await AuditEndpoints.WriteAsync(db, context.User, "Synced", "Order", order.Id.ToString(), $"ClientOperationId={input.ClientOperationId}");
                    results.Add(new { success = true, duplicate = false, orderId = order.Id, paid = HasPayment(input) });
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

    private static bool HasPayment(CreateOrderRequest input) => !string.IsNullOrWhiteSpace(input.PaymentMethod) || input.AmountTendered.HasValue || !string.IsNullOrWhiteSpace(input.PaymentReference);

    private static async Task<(bool Success, string? Error)> TryCreatePaymentAsync(CoreDbContext db, int orderId, CreateOrderRequest input, string username, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return (false, "Order not found.");
        return await AddPaymentAsync(db, order.Id, Math.Round(order.Total, 2), input, username, ct);
    }

    private static async Task<(bool Success, string? Error)> AddPaymentAsync(CoreDbContext db, int orderId, decimal total, CreateOrderRequest input, string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.PaymentMethod) || !PaymentMethods.Contains(input.PaymentMethod.Trim(), StringComparer.OrdinalIgnoreCase)) return (false, "Payment method must be Cash, Card, or Online.");
        if (!input.AmountTendered.HasValue || input.AmountTendered.Value <= 0) return (false, "AmountTendered must be greater than zero.");
        if (input.AmountTendered.Value > 1_000_000m) return (false, "AmountTendered is too large.");
        var method = PaymentMethods.First(x => x.Equals(input.PaymentMethod.Trim(), StringComparison.OrdinalIgnoreCase));
        var tendered = Math.Round(input.AmountTendered.Value, 2);
        total = Math.Round(total, 2);
        if (tendered < total) return (false, "Insufficient payment amount.");
        if (method is "Card" or "Online")
        {
            if (tendered != total) return (false, "Card and Online payments must equal the order total.");
            if (string.IsNullOrWhiteSpace(input.PaymentReference) || input.PaymentReference.Trim().Length > 200) return (false, "Card and Online payments require a valid payment reference.");
        }
        if (await db.Payments.AnyAsync(x => x.PosOrderId == orderId && x.Status == "Paid", ct)) return (false, "Order is already paid.");
        db.Payments.Add(new Payment { PosOrderId = orderId, AmountTendered = tendered, AmountPaid = total, ChangeAmount = method == "Cash" ? Math.Round(tendered - total, 2) : 0m, Method = method, Reference = (input.PaymentReference ?? string.Empty).Trim(), CollectedByUsername = username });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }
}
