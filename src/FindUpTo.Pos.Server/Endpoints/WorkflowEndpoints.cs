using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        app.MapPost("/api/orders/{id:int}/payment", async (int id, CollectPaymentRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (input.AmountTendered <= 0) return Results.BadRequest("Amount tendered must be greater than zero.");
            if (string.IsNullOrWhiteSpace(input.Method)) return Results.BadRequest("Payment method is required.");
            var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id);
            if (order is null) return Results.NotFound();
            if (order.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Cancelled orders cannot be paid.");

            var clientOperationId = input.ClientOperationId?.Trim() ?? "";
            if (clientOperationId.Length > 0)
            {
                if (clientOperationId.Length > 128) return Results.BadRequest("ClientOperationId is too long.");
                var previous = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == clientOperationId);
                if (previous is not null) return Results.Ok(previous);
            }

            if (await db.Payments.AnyAsync(x => x.PosOrderId == id && x.Status == "Paid")) return Results.Conflict("Order is already paid.");
            if (input.AmountTendered < order.Total) return Results.BadRequest($"Insufficient payment. Order total is {order.Total:0.00}.");
            var method = input.Method.Trim();
            var payment = new Payment
            {
                PosOrderId = id,
                AmountTendered = Math.Round(input.AmountTendered, 2),
                AmountPaid = Math.Round(order.Total, 2),
                ChangeAmount = method.Equals("Cash", StringComparison.OrdinalIgnoreCase) ? Math.Round(input.AmountTendered - order.Total, 2) : 0m,
                Method = method,
                Reference = input.Reference?.Trim() ?? "",
                ClientOperationId = clientOperationId,
                CollectedByUsername = user.Identity?.Name ?? "unknown"
            };
            db.Payments.Add(payment);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException) when (clientOperationId.Length > 0)
            {
                var previous = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.ClientOperationId == clientOperationId);
                if (previous is not null) return Results.Ok(previous);
                throw;
            }

            var hub = app.Services.GetRequiredService<IHubContext<PosHub>>();
            await hub.Clients.All.SendAsync("payment.updated", new { orderId = id, paymentId = payment.Id, status = payment.Status, method = payment.Method, amountPaid = payment.AmountPaid, changeAmount = payment.ChangeAmount });
            return Results.Ok(payment);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapGet("/api/orders/{id:int}/payments", async (int id, CoreDbContext db) =>
        {
            if (!await db.Orders.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.Payments.AsNoTracking().Where(x => x.PosOrderId == id).OrderByDescending(x => x.CreatedAtUtc).ToListAsync());
        }).RequireAuthorization();

        app.MapGet("/api/kitchen/orders", async (CoreDbContext db, string? status) =>
        {
            var allowed = new[] { "New", "Accepted", "Preparing", "Ready" };
            var q = db.Orders.AsNoTracking().Include(x => x.Items).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
            else q = q.Where(x => allowed.Contains(x.Status));
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
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
            else q = q.Where(x => x.Status == "Ready" || x.Status == "OutForDelivery");
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
