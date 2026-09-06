using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class WorkflowEndpoints
{
    private static readonly string[] PaymentMethods = ["Cash", "Card", "Online"];
    private static readonly string[] OrderTypes = ["Counter", "Dine In", "Pickup", "Delivery"];

    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        app.MapPost("/api/orders", async (CreateOrderRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            if (input.Items is null || input.Items.Count == 0) return Results.BadRequest("Order must contain at least one item.");
            if (input.Items.Count > 100) return Results.BadRequest("An order cannot contain more than 100 line items.");
            if (!OrderTypes.Contains(input.OrderType?.Trim() ?? "", StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Invalid order type.");
            if (input.ClientOperationId?.Trim().Length > 128) return Results.BadRequest("ClientOperationId is too long.");
            var operationId = input.ClientOperationId?.Trim() ?? "";
            if (operationId.Length > 0)
            {
                var duplicate = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == operationId);
                if (duplicate is not null) return Results.Ok(await db.Orders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == duplicate.OrderId));
            }
            if (input.CustomerId.HasValue && !await db.Customers.AnyAsync(x => x.Id == input.CustomerId.Value)) return Results.BadRequest("Customer not found.");
            if (input.TableId.HasValue && !await db.Tables.AnyAsync(x => x.Id == input.TableId.Value && x.Active)) return Results.BadRequest("Table not found or inactive.");
            if (input.OrderType.Equals("Dine In", StringComparison.OrdinalIgnoreCase) && !input.TableId.HasValue) return Results.BadRequest("A table is required for Dine In orders.");
            if (input.OrderType.Equals("Delivery", StringComparison.OrdinalIgnoreCase) && !input.CustomerId.HasValue) return Results.BadRequest("A customer is required for Delivery orders.");
            var ids = input.Items.Select(x => x.ProductId).Distinct().ToList();
            var products = await db.Products.AsNoTracking().Where(x => ids.Contains(x.Id) && x.Available).ToDictionaryAsync(x => x.Id);
            if (products.Count != ids.Count) return Results.BadRequest("One or more products are unavailable.");
            var order = new PosOrder { CustomerId = input.CustomerId, TableId = input.TableId, CreatedByUsername = user.Identity?.Name ?? "unknown", OrderType = OrderTypes.First(x => x.Equals(input.OrderType.Trim(), StringComparison.OrdinalIgnoreCase)), Notes = input.Notes?.Trim() ?? "" };
            foreach (var item in input.Items)
            {
                if (item.Quantity <= 0 || item.Quantity > 10000) return Results.BadRequest("Quantity must be between 1 and 10000.");
                var product = products[item.ProductId];
                order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = Math.Round(product.Price, 2), Quantity = item.Quantity, Notes = item.Notes?.Trim() ?? "", LineTotal = Math.Round(product.Price * item.Quantity, 2) });
            }
            order.Subtotal = Math.Round(order.Items.Sum(x => x.LineTotal), 2);
            var taxRate = await db.BusinessSettings.Select(x => x.TaxPercent).SingleAsync();
            order.Tax = Math.Round(order.Subtotal * taxRate / 100m, 2);
            order.Total = Math.Round(order.Subtotal + order.Tax, 2);
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            if (operationId.Length > 0)
            {
                db.SyncOperations.Add(new SyncOperation { ClientOperationId = operationId, OrderId = order.Id, CreatedByUsername = user.Identity?.Name ?? "unknown" });
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    await tx.RollbackAsync();
                    var raced = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == operationId);
                    if (raced is not null) return Results.Ok(await db.Orders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == raced.OrderId));
                    throw;
                }
            }
            await tx.CommitAsync();
            await AuditEndpoints.WriteAsync(db, user, "Created", "Order", order.Id.ToString(), operationId.Length == 0 ? "" : $"ClientOperationId={operationId}");
            await hub.Clients.All.SendAsync("order.updated", new { orderId = order.Id, status = order.Status, total = order.Total, updatedAtUtc = order.UpdatedAtUtc });
            return Results.Created($"/api/orders/{order.Id}", order);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter", "Waiter"));

        app.MapGet("/api/orders", async (CoreDbContext db, string? status) =>
        {
            var q = db.Orders.AsNoTracking().Include(x => x.Items).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status.Trim());
            return Results.Ok(await q.OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter", "Waiter", "Kitchen", "Rider"));

        app.MapPatch("/api/orders/{id:int}/status", async (int id, UpdateOrderStatusRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var allowed = new[] { "New", "Accepted", "Preparing", "Ready", "Served", "OutForDelivery", "Completed", "Cancelled" };
            if (!allowed.Contains(input.Status, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Invalid order status.");
            var order = await db.Orders.FindAsync(id); if (order is null) return Results.NotFound();
            var next = allowed.First(x => x.Equals(input.Status, StringComparison.OrdinalIgnoreCase));
            if (next == "Completed" && order.OrderType.Equals("Delivery", StringComparison.OrdinalIgnoreCase) && !order.Status.Equals("OutForDelivery", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Delivery orders must be out for delivery before completion.");
            if (next == "OutForDelivery" && (!order.OrderType.Equals("Delivery", StringComparison.OrdinalIgnoreCase) || !order.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase))) return Results.BadRequest("Only ready delivery orders can go out for delivery.");
            order.Status = next; order.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "StatusChanged", "Order", order.Id.ToString(), $"Status={order.Status}");
            await hub.Clients.All.SendAsync("order.updated", new { orderId = order.Id, status = order.Status, total = order.Total, updatedAtUtc = order.UpdatedAtUtc });
            return Results.Ok(order);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter", "Waiter", "Kitchen", "Rider"));

        app.MapPost("/api/orders/{id:int}/payment", async (int id, CollectPaymentRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (input.AmountTendered <= 0) return Results.BadRequest("Amount tendered must be greater than zero.");
            var method = input.Method?.Trim() ?? "";
            if (!PaymentMethods.Contains(method, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Payment method must be Cash, Card, or Online.");
            var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id);
            if (order is null) return Results.NotFound();
            if (order.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Cancelled orders cannot be paid.");
            var clientOperationId = input.ClientOperationId?.Trim() ?? "";
            if (clientOperationId.Length > 128) return Results.BadRequest("ClientOperationId is too long.");
            if (clientOperationId.Length > 0)
            {
                var previous = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == clientOperationId);
                if (previous is not null) return Results.Ok(previous);
            }
            if (await db.Payments.AnyAsync(x => x.PosOrderId == id && x.Status.Equals("Paid", StringComparison.OrdinalIgnoreCase))) return Results.Conflict("Order is already paid.");
            var orderTotal = Math.Round(order.Total, 2);
            var tendered = Math.Round(input.AmountTendered, 2);
            if (tendered < orderTotal) return Results.BadRequest($"Insufficient payment. Order total is {orderTotal:0.00}.");
            var isCash = method.Equals("Cash", StringComparison.OrdinalIgnoreCase);
            var reference = input.Reference?.Trim() ?? "";
            if (!isCash && tendered != orderTotal) return Results.BadRequest("Card and Online payments must equal the order total.");
            if (!isCash && string.IsNullOrWhiteSpace(reference)) return Results.BadRequest("A payment reference is required for Card and Online payments.");
            var payment = new Payment { PosOrderId = id, AmountTendered = tendered, AmountPaid = orderTotal, ChangeAmount = isCash ? Math.Round(tendered - orderTotal, 2) : 0m, Method = PaymentMethods.First(x => x.Equals(method, StringComparison.OrdinalIgnoreCase)), Reference = reference, ClientOperationId = clientOperationId, CollectedByUsername = user.Identity?.Name ?? "unknown" };
            db.Payments.Add(payment);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) when (clientOperationId.Length > 0)
            {
                var previous = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == clientOperationId);
                if (previous is not null) return Results.Ok(previous);
                throw;
            }
            await AuditEndpoints.WriteAsync(db, user, "Paid", "Payment", payment.Id.ToString(), $"OrderId={id};Method={payment.Method};Amount={payment.AmountPaid:0.00}");
            var hub = app.Services.GetRequiredService<IHubContext<PosHub>>();
            await hub.Clients.All.SendAsync("payment.updated", new { orderId = id, paymentId = payment.Id, status = payment.Status, method = payment.Method, amountPaid = payment.AmountPaid, changeAmount = payment.ChangeAmount });
            return Results.Ok(payment);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapGet("/api/orders/{id:int}/payments", async (int id, CoreDbContext db) =>
        {
            if (!await db.Orders.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.Payments.AsNoTracking().Where(x => x.PosOrderId == id).OrderByDescending(x => x.CreatedAtUtc).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapGet("/api/kitchen/orders", async (CoreDbContext db, string? status) =>
        {
            var allowed = new[] { "New", "Accepted", "Preparing", "Ready" };
            var q = db.Orders.AsNoTracking().Include(x => x.Items).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status); else q = q.Where(x => allowed.Contains(x.Status));
            return Results.Ok(await q.OrderBy(x => x.CreatedAtUtc).Take(200).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole("Kitchen", "Manager", "Owner", "Admin", "Counter"));

        app.MapPatch("/api/kitchen/orders/{id:int}/status", async (int id, UpdateOrderStatusRequest input, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var allowed = new[] { "Accepted", "Preparing", "Ready", "Cancelled" };
            if (!allowed.Contains(input.Status, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Invalid kitchen status.");
            var order = await db.Orders.FindAsync(id); if (order is null) return Results.NotFound();
            order.Status = allowed.First(x => string.Equals(x, input.Status, StringComparison.OrdinalIgnoreCase)); order.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("order.updated", new { orderId = order.Id, status = order.Status, total = order.Total, updatedAtUtc = order.UpdatedAtUtc });
            return Results.Ok(order);
        }).RequireAuthorization(p => p.RequireRole("Kitchen", "Manager", "Owner", "Admin"));

        app.MapGet("/api/waiter/orders", async (CoreDbContext db, string? status) =>
        {
            var q = db.Orders.AsNoTracking().Include(x => x.Items).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
            return Results.Ok(await q.Where(x => x.OrderType != "Delivery").OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole("Waiter", "Manager", "Owner", "Admin", "Counter"));

        app.MapPost("/api/waiter/orders/{id:int}/serve", async (int id, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var order = await db.Orders.FindAsync(id); if (order is null) return Results.NotFound();
            if (order.OrderType.Equals("Delivery", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Delivery orders must be handled by a rider.");
            if (!order.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Only ready orders can be served.");
            order.Status = "Served"; order.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("order.updated", new { orderId = order.Id, status = order.Status, total = order.Total, updatedAtUtc = order.UpdatedAtUtc });
            return Results.Ok(order);
        }).RequireAuthorization(p => p.RequireRole("Waiter", "Manager", "Owner", "Admin", "Counter"));

        app.MapGet("/api/rider/deliveries", async (CoreDbContext db, string? status) =>
        {
            var q = db.Orders.AsNoTracking().Include(x => x.Items).AsQueryable().Where(x => x.OrderType == "Delivery");
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status); else q = q.Where(x => x.Status == "Ready" || x.Status == "OutForDelivery");
            return Results.Ok(await q.OrderBy(x => x.CreatedAtUtc).Take(200).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole("Rider", "Manager", "Owner", "Admin", "Counter"));

        app.MapPatch("/api/rider/orders/{id:int}/status", async (int id, UpdateOrderStatusRequest input, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var allowed = new[] { "OutForDelivery", "Completed" };
            if (!allowed.Contains(input.Status, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Invalid rider status.");
            var order = await db.Orders.FindAsync(id); if (order is null) return Results.NotFound();
            if (!order.OrderType.Equals("Delivery", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Only delivery orders can use the rider workflow.");
            if (input.Status.Equals("OutForDelivery", StringComparison.OrdinalIgnoreCase) && !order.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Only ready delivery orders can go out for delivery.");
            if (input.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !order.Status.Equals("OutForDelivery", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Only out-for-delivery orders can be completed.");
            order.Status = allowed.First(x => string.Equals(x, input.Status, StringComparison.OrdinalIgnoreCase)); order.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("order.updated", new { orderId = order.Id, status = order.Status, total = order.Total, updatedAtUtc = order.UpdatedAtUtc });
            return Results.Ok(order);
        }).RequireAuthorization(p => p.RequireRole("Rider", "Manager", "Owner", "Admin"));
    }
}
